using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using UnityEngine;

namespace ErenshorSkills.Items
{
    /// <summary>
    /// Makes addon-crafted equipment and consumables participate in Erenshor's
    /// native SimPlayer systems. Equipment retains native slot, hand, relic,
    /// and upgrade scoring; food/drink uses a quantity-backed controller so a
    /// held item can never become an infinitely reusable spell.
    /// </summary>
    internal static class SimPlayerCraftedItems
    {
        private const int MaxHeldStack = 999;
        private static bool _eligibilityComplete;
        private static readonly FieldInfo TradeParentField =
            AccessTools.Field(typeof(SimTradeWindow), "parent");
        private static readonly MethodInfo NativeEquipEligibilityMethod =
            AccessTools.Method(typeof(ItemIcon), "DoInitialChecks",
                new[] { typeof(Item), typeof(ItemIcon) });

        private enum EquipmentProfile
        {
            Universal,
            HeavyArmor,
            MediumArmor,
            ClothArmor,
            MartialAccessory,
            AgileAccessory,
            CasterAccessory,
            MartialOneHand,
            Dagger,
            TwoHandMelee,
            TwoHandStaff,
            Bow,
            HeavyShield,
            MysticShield
        }

        private static readonly string[] HeavyArmorTokens =
        {
            "banded iron", "plate", "chainmail", "hauberk",
            "brigandine", "war harness", "war greaves",
            "dragonlord's greaves", "pauldrons"
        };

        private static readonly string[] MediumArmorTokens =
        {
            "leather", "hide", "scale", "ironbark", "bark vest",
            "patchwork vest", "reinforced vest", "reinforced leggings"
        };

        private static readonly string[] ClothArmorTokens =
        {
            "cloth", "silk", "weave", "woven", "robe", "cowl",
            "hood", "vestments"
        };

        private static readonly string[] MysticShieldTokens =
        {
            "bone", "wyrm", "void", "ghost", "moon", "rune"
        };

        private static readonly string[] NatureWeaponTokens =
        {
            "fernallan", "warden", "worldtree", "ironbark", "bone"
        };

        internal static void PrepareCraftedEquipment(Item item)
        {
            if (item == null || item.RequiredSlot == Item.SlotType.General)
                return;
            List<Class> allowed = BuildAllowedClasses(item);
            if (item.Classes == null) item.Classes = new List<Class>();
            else item.Classes.Clear();
            item.Classes.AddRange(allowed);
            item.SimPlayersCantGet = false;
        }

        internal static void RefreshCraftedEquipmentEligibility()
        {
            if (_eligibilityComplete) return;
            List<Item> equipment = ItemFactory.GetRegisteredItemsForTesting()
                .Where(IsCraftedEquippable).ToList();
            if (equipment.Count == 0) return;
            foreach (Item item in equipment) PrepareCraftedEquipment(item);
            _eligibilityComplete =
                Necromancer.NecromancerCatalog.NecromancerClass != null &&
                equipment.All(item => item.Classes != null &&
                    item.Classes.Count > 0 && !item.SimPlayersCantGet &&
                    SameClasses(item.Classes, BuildAllowedClasses(item)));
        }

        internal static bool IsCraftedEquippable(Item item)
        {
            if (item == null || item.RequiredSlot == Item.SlotType.General)
                return false;
            string[] tradeskills =
                { "Smithing", "Fletching", "Jewelcraft", "Tailoring" };
            foreach (string tradeskill in tradeskills)
                if (Skills.Tradeskills.GetRecipesForSkillPublic(tradeskill)
                    .Any(recipe => string.Equals(recipe.Name, item.ItemName,
                        StringComparison.OrdinalIgnoreCase)))
                    return true;
            return false;
        }

        private static List<Class> BuildAllowedClasses(Item item)
        {
            var result = new List<Class>();
            ClassDB database = GameData.ClassDB;
            if (item == null || database == null) return result;

            EquipmentProfile profile = GetEquipmentProfile(item);
            switch (profile)
            {
                case EquipmentProfile.Universal:
                    AddClass(result, database.Paladin);
                    AddClass(result, database.Duelist);
                    AddClass(result, database.Arcanist);
                    AddClass(result, database.Druid);
                    AddClass(result, database.Stormcaller);
                    AddClass(result, database.Reaver);
                    break;
                case EquipmentProfile.HeavyArmor:
                    AddClass(result, database.Paladin);
                    AddClass(result, database.Duelist);
                    AddClass(result, database.Reaver);
                    break;
                case EquipmentProfile.MediumArmor:
                    AddClass(result, database.Paladin);
                    AddClass(result, database.Duelist);
                    AddClass(result, database.Druid);
                    AddClass(result, database.Stormcaller);
                    AddClass(result, database.Reaver);
                    break;
                case EquipmentProfile.ClothArmor:
                    // Native cloth is broadly wearable; class desirability is
                    // determined by its stats rather than an armor prohibition.
                    AddClass(result, database.Paladin);
                    AddClass(result, database.Duelist);
                    AddClass(result, database.Arcanist);
                    AddClass(result, database.Druid);
                    AddClass(result, database.Stormcaller);
                    AddClass(result, database.Reaver);
                    break;
                case EquipmentProfile.CasterAccessory:
                    AddClass(result, database.Arcanist);
                    AddClass(result, database.Druid);
                    AddClass(result, database.Stormcaller);
                    break;
                case EquipmentProfile.MartialAccessory:
                    AddClass(result, database.Paladin);
                    AddClass(result, database.Duelist);
                    AddClass(result, database.Reaver);
                    break;
                case EquipmentProfile.AgileAccessory:
                    AddClass(result, database.Duelist);
                    AddClass(result, database.Druid);
                    AddClass(result, database.Stormcaller);
                    AddClass(result, database.Reaver);
                    break;
                case EquipmentProfile.MartialOneHand:
                    AddClass(result, database.Paladin);
                    AddClass(result, database.Duelist);
                    AddClass(result, database.Reaver);
                    if (HasAnyToken(item, NatureWeaponTokens))
                        AddClass(result, database.Druid);
                    break;
                case EquipmentProfile.Dagger:
                    // Mirrors the native dagger baseline: every melee/caster
                    // class except the bow-locked Stormcaller can use one.
                    AddClass(result, database.Paladin);
                    AddClass(result, database.Duelist);
                    AddClass(result, database.Arcanist);
                    AddClass(result, database.Druid);
                    AddClass(result, database.Reaver);
                    break;
                case EquipmentProfile.TwoHandMelee:
                    AddClass(result, database.Paladin);
                    AddClass(result, database.Reaver);
                    break;
                case EquipmentProfile.TwoHandStaff:
                    AddClass(result, database.Arcanist);
                    AddClass(result, database.Druid);
                    break;
                case EquipmentProfile.Bow:
                    AddClass(result, database.Stormcaller);
                    break;
                case EquipmentProfile.HeavyShield:
                    AddClass(result, database.Paladin);
                    AddClass(result, database.Reaver);
                    break;
                case EquipmentProfile.MysticShield:
                    AddClass(result, database.Paladin);
                    AddClass(result, database.Arcanist);
                    AddClass(result, database.Druid);
                    AddClass(result, database.Stormcaller);
                    AddClass(result, database.Reaver);
                    break;
            }

            // Necromancer deliberately inherits only the crafted profiles that
            // permit Arcanists. It can use cloth, caster jewelry, daggers,
            // staves, and mystical shields, but never plate or 2H swords.
            if (result.Contains(database.Arcanist))
                AddClass(result,
                    Necromancer.NecromancerCatalog.NecromancerClass);
            return result;
        }

        private static EquipmentProfile GetEquipmentProfile(Item item)
        {
            if (item == null) return EquipmentProfile.Universal;
            if (item.Shield || item.RequiredSlot == Item.SlotType.Secondary)
                return HasAnyToken(item, MysticShieldTokens)
                    ? EquipmentProfile.MysticShield
                    : EquipmentProfile.HeavyShield;

            switch (item.ThisWeaponType)
            {
                case Item.WeaponType.OneHandDagger:
                    return EquipmentProfile.Dagger;
                case Item.WeaponType.TwoHandMelee:
                    return EquipmentProfile.TwoHandMelee;
                case Item.WeaponType.TwoHandStaff:
                    return EquipmentProfile.TwoHandStaff;
                case Item.WeaponType.TwoHandBow:
                    return EquipmentProfile.Bow;
                case Item.WeaponType.OneHandMelee:
                    return EquipmentProfile.MartialOneHand;
            }
            if (item.IsBow) return EquipmentProfile.Bow;
            if (item.WeaponDmg > 0) return EquipmentProfile.MartialOneHand;

            if (!IsAccessorySlot(item.RequiredSlot))
            {
                if (HasAnyToken(item, HeavyArmorTokens))
                    return EquipmentProfile.HeavyArmor;
                if (HasAnyToken(item, ClothArmorTokens))
                    return EquipmentProfile.ClothArmor;
                if (HasAnyToken(item, MediumArmorTokens))
                    return EquipmentProfile.MediumArmor;
            }
            return GetStatProfile(item);
        }

        private static EquipmentProfile GetStatProfile(Item item)
        {
            int populated = CountPositive(item.Str, item.End, item.Dex,
                item.Agi, item.Int, item.Wis, item.Cha);
            int martial = item.Str * 2 + item.End * 2 + item.AC +
                Mathf.RoundToInt(item.HP / 10f);
            int agile = item.Dex * 2 + item.Agi * 2;
            int caster = item.Int * 2 + item.Wis * 2 + item.Cha +
                Mathf.RoundToInt(item.Mana / 10f) + item.Res;
            if (populated >= 5 || (populated == 0 && martial == 0 &&
                    agile == 0 && caster == 0) ||
                (item.Cha > 0 && martial == 0 && agile == 0 &&
                    item.Int == 0 && item.Wis == 0))
                return EquipmentProfile.Universal;
            if (caster > martial && caster >= agile)
                return EquipmentProfile.CasterAccessory;
            if (agile > martial && agile > caster)
                return EquipmentProfile.AgileAccessory;
            return EquipmentProfile.MartialAccessory;
        }

        private static bool IsAccessorySlot(Item.SlotType slot)
        {
            return slot == Item.SlotType.Neck || slot == Item.SlotType.Ring ||
                slot == Item.SlotType.Charm || slot == Item.SlotType.Back ||
                slot == Item.SlotType.Waist;
        }

        private static bool HasAnyToken(Item item, IEnumerable<string> tokens)
        {
            string text = ((item?.ItemName ?? "") + " " +
                (item?.Lore ?? "")).ToLowerInvariant();
            return tokens.Any(token => text.Contains(token));
        }

        private static int CountPositive(params int[] values)
        {
            return values.Count(value => value > 0);
        }

        private static void AddClass(ICollection<Class> values, Class value)
        {
            if (value != null && !values.Contains(value)) values.Add(value);
        }

        private static bool SameClasses(IEnumerable<Class> left,
            IEnumerable<Class> right)
        {
            var leftSet = new HashSet<Class>(left.Where(value => value != null));
            var rightSet = new HashSet<Class>(right.Where(value => value != null));
            return leftSet.SetEquals(rightSet);
        }

        internal static string GetRestrictionProfileForTesting(Item item)
        {
            return GetEquipmentProfile(item).ToString();
        }

        internal static bool IsCraftedNecromancerUpgrade(SimPlayer sim,
            Item item)
        {
            if (sim?.MyStats == null || sim.MyEquipment == null ||
                item == null || !IsCraftedEquippable(item) ||
                !Necromancer.NecromancerCatalog.IsNecromancerClass(
                    sim.MyStats.CharacterClass)) return false;

            PrepareCraftedEquipment(item);
            if (item.Classes == null || !item.Classes.Any(
                    Necromancer.NecromancerCatalog.IsNecromancerClass) ||
                item.SimPlayersCantGet || item == GameData.PlayerInv?.Empty)
                return false;
            if (item.Relic && sim.MyEquipment.Any(slot => slot != null &&
                    ReferenceEquals(slot.MyItem, item))) return false;
            if (sim.AllHeldItems?.Any(entry => entry != null &&
                    string.Equals(entry.ID, item.Id,
                        StringComparison.OrdinalIgnoreCase)) == true)
                return false;

            SimInvSlot primary = sim.MyEquipment.FirstOrDefault(slot =>
                slot != null && slot.ThisSlotType == Item.SlotType.Primary);
            bool primaryIsTwoHand = IsTwoHand(primary?.MyItem);
            foreach (SimInvSlot slot in sim.MyEquipment)
            {
                if (slot == null || !SlotAcceptsItem(slot.ThisSlotType, item))
                    continue;
                if (slot.ThisSlotType == Item.SlotType.Secondary &&
                    primaryIsTwoHand) continue;
                Item current = slot.MyItem;
                if (current == null || current == GameData.PlayerInv.Empty)
                    return true;
                if (ReferenceEquals(current, item) ||
                    item.ItemLevel <= current.ItemLevel + 3) continue;
                if (GetNecromancerItemScore(item) >
                    GetNecromancerItemScore(current) * 1.02f)
                    return true;
            }
            return false;
        }

        private static bool SlotAcceptsItem(Item.SlotType slot, Item item)
        {
            return item != null && (slot == item.RequiredSlot ||
                (item.RequiredSlot == Item.SlotType.PrimaryOrSecondary &&
                 (slot == Item.SlotType.Primary ||
                  slot == Item.SlotType.Secondary)));
        }

        private static bool IsTwoHand(Item item)
        {
            return item != null &&
                (item.ThisWeaponType == Item.WeaponType.TwoHandMelee ||
                 item.ThisWeaponType == Item.WeaponType.TwoHandStaff ||
                 item.ThisWeaponType == Item.WeaponType.TwoHandBow);
        }

        private static float GetNecromancerItemScore(Item item)
        {
            if (item == null || item == GameData.PlayerInv?.Empty) return 0f;
            float weaponDps = item.WeaponDmg > 0 && item.WeaponDly > 0f
                ? item.WeaponDmg / item.WeaponDly : 0f;
            float proc = item.WeaponProcOnHit?.TargetDamage ?? 0f;
            return item.ItemLevel * 4f + item.Int * 4f + item.Wis * 2f +
                item.Cha + item.Res * 2f + item.Mana / 8f + item.HP / 20f +
                item.AC * 0.5f + weaponDps * 8f + proc * 0.08f;
        }

        private static IEnumerable<Class> GetPlayableClasses()
        {
            ClassDB database = GameData.ClassDB;
            if (database != null)
            {
                yield return database.Paladin;
                yield return database.Duelist;
                yield return database.Arcanist;
                yield return database.Druid;
                yield return database.Stormcaller;
                yield return database.Reaver;
            }
            yield return Necromancer.NecromancerCatalog.NecromancerClass;
        }

        internal static bool IsCraftedConsumable(Item item,
            out string category)
        {
            category = null;
            if (item == null || !ItemFactory.IsCustomItem(item) ||
                item.RequiredSlot != Item.SlotType.General ||
                !item.Stackable || !item.Disposable ||
                item.ItemEffectOnClick == null ||
                !ItemFactory.ConsumableBuffs.TryGetValue(item.ItemName,
                    out var metadata)) return false;
            category = metadata.category;
            return string.Equals(category, "Food",
                    StringComparison.OrdinalIgnoreCase) ||
                string.Equals(category, "Drink",
                    StringComparison.OrdinalIgnoreCase);
        }

        internal static bool TryHandleConsumableTrade(SimTradeWindow window)
        {
            ItemIcon slot = window?.LootSlot;
            Item item = slot?.MyItem;
            if (!IsCraftedConsumable(item, out string category)) return false;

            Transform parent = TradeParentField?.GetValue(window) as Transform;
            SimPlayer sim = parent?.GetComponent<SimPlayer>();
            if (sim == null || !sim.VerifyLoadComplete || sim.AllHeldItems == null)
                return false;

            int quantity = Mathf.Clamp(slot.Quantity, 1, MaxHeldStack);
            int oldQuantity = GetHeldQuantity(sim.AllHeldItems, item.Id);
            bool savedToSim = false;
            try
            {
                SetHeldQuantity(sim.AllHeldItems, item.Id,
                    Mathf.Clamp(oldQuantity + quantity, 1, MaxHeldStack));
                sim.SaveSim();
                savedToSim = true;

                slot.MyItem = GameData.PlayerInv.Empty;
                slot.Quantity = 1;
                slot.UpdateSlotImage();
                window.CloseWindow(new List<ItemIcon>());
                SimPlayerCraftedConsumableController.Attach(sim);

                string simName = sim.GetComponent<NPC>()?.NPCName ??
                    sim.transform.name;
                UpdateSocialLog.LogAdd(new ChatLogLine(
                    $"{simName} accepts {quantity} {item.ItemName} for later use.",
                    ChatLogLine.LogType.SystemMessages, "#CDDC39"));
                SkillsPlugin.Log.LogInfo(
                    $"Sim consumable trade: {simName} accepted {quantity} " +
                    $"{item.ItemName} ({category}); held={oldQuantity + quantity}.");
                return true;
            }
            catch (Exception ex)
            {
                if (!savedToSim)
                    SetHeldQuantity(sim.AllHeldItems, item.Id, oldQuantity);
                else
                {
                    // The item is already durably owned by the Sim. Never put
                    // it back in the trade slot and create a second copy.
                    slot.MyItem = GameData.PlayerInv.Empty;
                    slot.Quantity = 1;
                    try { slot.UpdateSlotImage(); } catch { }
                    try { window.CloseWindow(new List<ItemIcon>()); } catch { }
                }
                SkillsPlugin.Log.LogError(
                    $"Sim consumable trade rolled back for {item.ItemName}: {ex}");
                return savedToSim;
            }
        }

        private static int GetHeldQuantity(List<ItemSaveData> held,
            string itemId)
        {
            return held?.Where(entry => entry != null &&
                    string.Equals(entry.ID, itemId,
                        StringComparison.OrdinalIgnoreCase))
                .Sum(entry => Mathf.Max(0, entry.Quality)) ?? 0;
        }

        private static void SetHeldQuantity(List<ItemSaveData> held,
            string itemId, int quantity)
        {
            if (held == null || string.IsNullOrWhiteSpace(itemId)) return;
            held.RemoveAll(entry => entry != null &&
                string.Equals(entry.ID, itemId,
                    StringComparison.OrdinalIgnoreCase));
            if (quantity > 0)
                held.Add(new ItemSaveData(itemId,
                    Mathf.Clamp(quantity, 1, MaxHeldStack)));
        }

        internal static void RemoveConsumableSpellsFromNativeAi(SimPlayer sim)
        {
            if (sim == null) return;
            NPC npc = sim.GetComponent<NPC>();
            npc?.MyBuffSpells?.RemoveAll(ItemFactory.IsConsumableSpell);
            // Preserve Spellbook indices. Native healer/buff AI resolves a
            // spell's index and immediately reads its cooldown; shortening the
            // list in a postfix can invalidate those fixed-index reads. The
            // actionable MyBuffSpells list is sufficient to prevent infinite
            // native casting while the quantity-backed controller owns use.
            SimPlayerCraftedConsumableController.Attach(sim);
        }

        internal static int ReconcileLoadedSimPlayers()
        {
            int count = 0;
            try
            {
                foreach (SimPlayer sim in
                    UnityEngine.Object.FindObjectsOfType<SimPlayer>())
                {
                    if (sim == null || !sim.VerifyLoadComplete) continue;
                    RemoveConsumableSpellsFromNativeAi(sim);
                    count++;
                }
            }
            catch (Exception ex)
            {
                SkillsPlugin.Log.LogWarning(
                    "Loaded SimPlayer consumable reconciliation failed safely: " +
                    ex.Message);
            }
            return count;
        }

        private static bool HasActiveCategory(Stats stats, string category)
        {
            if (stats?.StatusEffects == null) return false;
            string tag = "[CS_" + category + "]";
            int count = Mathf.Min(30, stats.StatusEffects.Length);
            for (int index = 0; index < count; index++)
            {
                Spell effect = stats.StatusEffects[index]?.Effect;
                if (effect != null && !string.IsNullOrEmpty(effect.SpellName) &&
                    effect.SpellName.IndexOf(tag,
                        StringComparison.OrdinalIgnoreCase) >= 0)
                    return true;
            }
            return false;
        }

        private static int FindFreeStatusSlot(Stats stats)
        {
            if (stats?.StatusEffects == null) return -1;
            int count = Mathf.Min(30, stats.StatusEffects.Length);
            for (int index = 0; index < count; index++)
                if (stats.StatusEffects[index] != null &&
                    stats.StatusEffects[index].Effect == null)
                    return index;
            return -1;
        }

        internal static bool TryConsumeNext(SimPlayer sim, string category,
            out string detail)
        {
            detail = "no eligible held consumable";
            if (sim == null || !sim.VerifyLoadComplete || sim.MyStats == null ||
                sim.AllHeldItems == null || sim.GetComponent<Character>() == null)
                return false;
            Character character = sim.GetComponent<Character>();
            if (!character.Alive || HasActiveCategory(sim.MyStats, category))
            {
                detail = "category already active or SimPlayer unavailable";
                return false;
            }

            ItemSaveData heldEntry = null;
            Item heldItem = null;
            foreach (ItemSaveData entry in sim.AllHeldItems.ToList())
            {
                if (entry == null || entry.Quality <= 0) continue;
                Item candidate = GameData.ItemDB?.GetItemByID(entry.ID);
                if (!IsCraftedConsumable(candidate,
                        out string candidateCategory) ||
                    !string.Equals(candidateCategory, category,
                        StringComparison.OrdinalIgnoreCase)) continue;
                heldEntry = entry;
                heldItem = candidate;
                break;
            }
            if (heldEntry == null || heldItem == null) return false;
            if (FindFreeStatusSlot(sim.MyStats) < 0)
            {
                detail = "no free status-effect slot; quantity preserved";
                return false;
            }

            Spell spell = heldItem.ItemEffectOnClick;
            string tag = "[CS_" + category + "]";
            if ((spell.SpellName ?? "").IndexOf(tag,
                    StringComparison.OrdinalIgnoreCase) < 0)
                spell.SpellName = heldItem.ItemName + " " + tag;

            int quantityBefore = heldEntry.Quality;
            int healthBefore = sim.MyStats.CurrentHP;
            var durationSnapshots = new List<KeyValuePair<StatusEffect, float>>();
            try
            {
                sim.MyStats.AddStatusEffectNoChecks(spell, false, 0, character);
                StatusEffect applied = sim.MyStats.StatusEffects
                    .Take(Mathf.Min(30, sim.MyStats.StatusEffects.Length))
                    .FirstOrDefault(status => status != null &&
                        ReferenceEquals(status.Effect, spell));
                if (applied == null)
                {
                    detail = "buff did not take hold; quantity preserved";
                    return false;
                }

                if ((heldItem.Lore ?? "").IndexOf(
                        "extends active buffs by 50%",
                        StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    foreach (StatusEffect status in sim.MyStats.StatusEffects)
                    {
                        Spell effect = status?.Effect;
                        if (effect == null || ReferenceEquals(effect, spell) ||
                            effect.WornEffect ||
                            effect.Type != Spell.SpellType.Beneficial ||
                            status.Duration <= 0f) continue;
                        durationSnapshots.Add(
                            new KeyValuePair<StatusEffect, float>(status,
                                status.Duration));
                        status.Duration = Mathf.Min(status.Duration * 1.5f,
                            1000000f);
                    }
                }
                if ((heldItem.Lore ?? "").IndexOf("full hp restore",
                        StringComparison.OrdinalIgnoreCase) >= 0)
                    sim.MyStats.CurrentHP = sim.MyStats.CurrentMaxHP;

                heldEntry.Quality--;
                if (heldEntry.Quality <= 0)
                    sim.AllHeldItems.Remove(heldEntry);
                sim.SaveSim();

                string simName = sim.GetComponent<NPC>()?.NPCName ??
                    sim.transform.name;
                UpdateSocialLog.CombatLogAdd(new ChatLogLine(
                    $"{simName} consumes {heldItem.ItemName} ({category}).",
                    ChatLogLine.LogType.SystemMessages, "#CDDC39"));
                detail = $"{heldItem.ItemName} {quantityBefore}->" +
                    $"{quantityBefore - 1}; {category} applied once";
                return true;
            }
            catch (Exception ex)
            {
                heldEntry.Quality = quantityBefore;
                if (!sim.AllHeldItems.Contains(heldEntry))
                    sim.AllHeldItems.Add(heldEntry);
                sim.MyStats.CurrentHP = healthBefore;
                foreach (var snapshot in durationSnapshots)
                    if (snapshot.Key != null) snapshot.Key.Duration = snapshot.Value;
                for (int index = 0; index < Mathf.Min(30,
                    sim.MyStats.StatusEffects.Length); index++)
                    if (sim.MyStats.StatusEffects[index] != null &&
                        ReferenceEquals(sim.MyStats.StatusEffects[index].Effect,
                            spell))
                    {
                        sim.MyStats.RemoveStatusEffect(index);
                        break;
                    }
                sim.MyStats.CalcStats();
                detail = "transaction rolled back: " + ex.Message;
                SkillsPlugin.Log.LogError(
                    $"Sim consumable transaction rolled back: {ex}");
                return false;
            }
        }

        internal static bool RunCatalogSelfTest(out string detail)
        {
            var errors = new List<string>();
            List<Class> playable = GetPlayableClasses()
                .Where(value => value != null).Distinct().ToList();
            List<Item> equipment = ItemFactory.GetRegisteredItemsForTesting()
                .Where(IsCraftedEquippable)
                .ToList();
            var profileCounts = new Dictionary<EquipmentProfile, int>();
            var classCounts = playable.ToDictionary(value => value,
                value => 0);
            Class arcanist = GameData.ClassDB?.Arcanist;
            Class stormcaller = GameData.ClassDB?.Stormcaller;
            Class duelist = GameData.ClassDB?.Duelist;
            Class necromancer =
                Necromancer.NecromancerCatalog.NecromancerClass;
            foreach (Item item in equipment)
            {
                EquipmentProfile profile = GetEquipmentProfile(item);
                profileCounts[profile] = profileCounts.TryGetValue(profile,
                    out int profileCount) ? profileCount + 1 : 1;
                List<Class> expected = BuildAllowedClasses(item);
                if (expected.Count == 0)
                    errors.Add(item.ItemName + " has no eligible class");
                if (item.Classes == null || item.Classes.Count != expected.Count ||
                    !SameClasses(item.Classes, expected))
                    errors.Add(item.ItemName + " class restriction drifted (" +
                        profile + ")");
                else
                    foreach (Class value in expected)
                        if (value != null && classCounts.ContainsKey(value))
                            classCounts[value]++;
                if (item.Classes != null && item.Classes.Any(value => value == null))
                    errors.Add(item.ItemName + " has a null class entry");
                if (item.SimPlayersCantGet)
                    errors.Add(item.ItemName + " blocks SimPlayers");

                bool hasArcanist = arcanist != null &&
                    item.Classes?.Contains(arcanist) == true;
                bool hasNecromancer = necromancer != null &&
                    item.Classes?.Contains(necromancer) == true;
                if (necromancer != null && hasArcanist != hasNecromancer)
                    errors.Add(item.ItemName +
                        " violates Arcanist/Necromancer caster inheritance");
                if ((profile == EquipmentProfile.HeavyArmor ||
                     profile == EquipmentProfile.TwoHandMelee) &&
                    (hasArcanist || hasNecromancer))
                    errors.Add(item.ItemName +
                        " incorrectly permits a cloth caster");
                if (profile == EquipmentProfile.Bow &&
                    (item.Classes == null || item.Classes.Count != 1 ||
                     stormcaller == null ||
                     !item.Classes.Contains(stormcaller)))
                    errors.Add(item.ItemName + " is not Stormcaller-only");
                if (profile == EquipmentProfile.Dagger && stormcaller != null &&
                    item.Classes?.Contains(stormcaller) == true)
                    errors.Add(item.ItemName +
                        " incorrectly permits Stormcaller dagger use");
            }

            List<Item> offhands = ItemFactory
                .GetCraftedOffhandWeaponsForTesting();
            foreach (Item item in offhands)
            {
                if (item.RequiredSlot != Item.SlotType.PrimaryOrSecondary ||
                    (item.ThisWeaponType != Item.WeaponType.OneHandMelee &&
                     item.ThisWeaponType != Item.WeaponType.OneHandDagger))
                    errors.Add(item.ItemName +
                        " is not a legal one-handed offhand");
                if (duelist == null || item.Classes == null ||
                    !item.Classes.Contains(duelist))
                    errors.Add(item.ItemName +
                        " lacks Duelist offhand eligibility");
            }

            if (!profileCounts.ContainsKey(EquipmentProfile.HeavyArmor))
                errors.Add("no heavy-armor restriction probe exists");
            if (!profileCounts.ContainsKey(EquipmentProfile.TwoHandMelee))
                errors.Add("no two-handed melee restriction probe exists");
            if (!profileCounts.ContainsKey(EquipmentProfile.ClothArmor))
                errors.Add("no cloth-armor caster probe exists");
            if (necromancer == null)
                errors.Add("Necromancer class is unavailable for restrictions");

            int food = 0;
            int drink = 0;
            foreach (var pair in ItemFactory.ConsumableBuffs)
            {
                Item item = ItemFactory.GetItem(pair.Key);
                if (!IsCraftedConsumable(item, out string category))
                    errors.Add(pair.Key + " is not a complete Sim consumable");
                else if (string.Equals(category, "Food",
                    StringComparison.OrdinalIgnoreCase)) food++;
                else drink++;
            }

            var probe = new List<ItemSaveData>();
            Item sample = ItemFactory.ConsumableBuffs.Keys
                .Select(ItemFactory.GetItem).FirstOrDefault(item => item != null);
            if (sample != null)
            {
                SetHeldQuantity(probe, sample.Id, 3);
                int before = GetHeldQuantity(probe, sample.Id);
                SetHeldQuantity(probe, sample.Id, before - 1);
                if (before != 3 || GetHeldQuantity(probe, sample.Id) != 2 ||
                    probe.Count != 1)
                    errors.Add("quantity-backed consumable transaction failed");
            }
            else errors.Add("no consumable sample available");

            string profiles = string.Join(",", profileCounts
                .OrderBy(pair => pair.Key.ToString())
                .Select(pair => pair.Key + "=" + pair.Value));
            string coverage = string.Join(",", classCounts
                .OrderBy(pair => pair.Key.ClassName)
                .Select(pair => pair.Key.ClassName + "=" + pair.Value));
            detail = errors.Count == 0
                ? $"{equipment.Count} crafted equippables use exact native-style " +
                  $"class matrices ({profiles}); eligibility {coverage}; " +
                  $"{offhands.Count} legal offhands; {food} food and {drink} " +
                  "drink templates are quantity-backed; 3->2 decrement probe passed"
                : string.Join("; ", errors.Take(20));
            return errors.Count == 0;
        }

        private static bool VerifyNativeNecromancerRestrictions(
            out string detail)
        {
            detail = "static class matrix verified";
            if (!Necromancer.NecromancerCatalog.IsNecromancerPlayer())
                return true;
            if (NativeEquipEligibilityMethod == null ||
                GameData.PlayerInv?.StoredSlots == null ||
                GameData.PlayerInv.EquipmentSlots == null)
            {
                detail = "native item eligibility probe is unavailable";
                return false;
            }

            ItemIcon source = GameData.PlayerInv.StoredSlots.FirstOrDefault(slot =>
                slot != null && slot.ThisSlotType == Item.SlotType.General);
            if (source == null)
            {
                detail = "no inventory slot is available for native rejection probes";
                return false;
            }

            var probes = ItemFactory.GetRegisteredItemsForTesting()
                .Where(IsCraftedEquippable)
                .Where(item => GetEquipmentProfile(item) ==
                        EquipmentProfile.HeavyArmor ||
                    GetEquipmentProfile(item) == EquipmentProfile.TwoHandMelee)
                .GroupBy(GetEquipmentProfile)
                .Select(group => group.OrderBy(item => item.ItemLevel).First())
                .ToList();
            var errors = new List<string>();
            var rejected = new List<string>();
            Item original = source.MyItem;
            int originalQuantity = source.Quantity;
            try
            {
                foreach (Item item in probes)
                {
                    Item.SlotType targetType = item.RequiredSlot ==
                            Item.SlotType.PrimaryOrSecondary
                        ? Item.SlotType.Primary : item.RequiredSlot;
                    ItemIcon target = GameData.PlayerInv.EquipmentSlots
                        .FirstOrDefault(slot => slot != null &&
                            slot.ThisSlotType == targetType);
                    if (target == null)
                    {
                        errors.Add(item.ItemName + " has no native target slot");
                        continue;
                    }
                    source.MyItem = item;
                    source.Quantity = 1;
                    object result = NativeEquipEligibilityMethod.Invoke(source,
                        new object[] { item, target });
                    bool allowed = result is bool value && value;
                    if (allowed)
                        errors.Add(item.ItemName +
                            " was accepted by a Necromancer");
                    else
                        rejected.Add(item.ItemName + "[" +
                            GetEquipmentProfile(item) + "]");
                }
            }
            catch (Exception ex)
            {
                errors.Add("native rejection probe threw: " +
                    (ex is TargetInvocationException invocation
                        ? (invocation.InnerException ?? invocation).Message
                        : ex.Message));
            }
            finally
            {
                source.MyItem = original;
                source.Quantity = originalQuantity;
                try { source.UpdateSlotImage(); } catch { }
            }

            if (probes.Count != 2)
                errors.Add("expected heavy-armor and two-handed melee probes");
            detail = errors.Count == 0
                ? "native Necromancer rejection passed for " +
                  string.Join(", ", rejected)
                : string.Join("; ", errors);
            return errors.Count == 0;
        }

        private static bool IsConsumableTradePatchActive()
        {
            MethodInfo original = AccessTools.Method(typeof(SimTradeWindow),
                nameof(SimTradeWindow.CompleteTrade));
            HarmonyLib.Patches patches = original == null
                ? null : Harmony.GetPatchInfo(original);
            return patches?.Prefixes?.Any(patch =>
                string.Equals(patch.owner, SkillsPlugin.PluginGuid,
                    StringComparison.Ordinal) &&
                patch.PatchMethod?.DeclaringType ==
                    typeof(Patch_SimTradeCraftedConsumables)) == true;
        }

        private static bool ProbeConsumableTradeHandler(SimPlayer sim,
            Item item, int quantity, out string detail)
        {
            detail = "trade probe is unavailable";
            SimTradeWindow window = GameData.SimTrade;
            if (sim == null || item == null || window?.LootSlot == null ||
                window.WindowParent == null || TradeParentField == null)
                return false;
            if (GameData.Trading || window.WindowParent.activeSelf)
            {
                detail = "a real trade is already active";
                return false;
            }
            if (!IsConsumableTradePatchActive())
            {
                detail = "CompleteTrade consumable prefix is not installed";
                return false;
            }

            object parentBefore = TradeParentField.GetValue(window);
            Item slotItemBefore = window.LootSlot.MyItem;
            int slotQuantityBefore = window.LootSlot.Quantity;
            bool tradingBefore = GameData.Trading;
            bool windowBefore = window.WindowParent.activeSelf;
            int heldBefore = GetHeldQuantity(sim.AllHeldItems, item.Id);
            try
            {
                TradeParentField.SetValue(window, sim.transform);
                window.LootSlot.MyItem = item;
                window.LootSlot.Quantity = quantity;
                bool handled = TryHandleConsumableTrade(window);
                int heldAfter = GetHeldQuantity(sim.AllHeldItems, item.Id);
                bool slotCleared = window.LootSlot.MyItem ==
                    GameData.PlayerInv.Empty;
                detail = $"handled={handled}; held={heldBefore}->" +
                    $"{heldAfter}; slotCleared={slotCleared}; patch=installed";
                return handled && slotCleared &&
                    heldAfter == heldBefore + quantity;
            }
            finally
            {
                TradeParentField.SetValue(window, parentBefore);
                window.LootSlot.MyItem = slotItemBefore;
                window.LootSlot.Quantity = slotQuantityBefore;
                GameData.Trading = tradingBefore;
                window.WindowParent.SetActive(windowBefore);
                try { window.LootSlot.UpdateSlotImage(); } catch { }
            }
        }

        internal static bool RunLiveSelfTest(SimPlayer sim, out string detail)
        {
            if (sim == null || !sim.VerifyLoadComplete || sim.MyStats == null ||
                sim.MyEquipment == null || sim.AllHeldItems == null)
            {
                detail = "no fully loaded SimPlayer is available";
                return false;
            }

            var errors = new List<string>();
            Inventory simInventory = sim.GetComponent<Inventory>();
            if (simInventory == null)
                errors.Add("SimPlayer inventory component is unavailable");
            if (!VerifyNativeNecromancerRestrictions(
                    out string restrictionDetail))
                errors.Add(restrictionDetail);
            var equipmentSnapshot = sim.MyEquipment.Select(slot => new
                {
                    Slot = slot,
                    Item = slot?.MyItem,
                    Quantity = slot?.Quant ?? 1
                }).ToList();
            List<ItemSaveData> heldSnapshot = sim.AllHeldItems
                .Where(entry => entry != null)
                .Select(entry => new ItemSaveData(entry.ID, entry.Quality))
                .ToList();
            List<Item> storedSnapshot = sim.StoredItems == null
                ? null : new List<Item>(sim.StoredItems);
            var appliedSpells = new HashSet<Spell>();
            Spell[] spellDatabaseSnapshot = GameData.SpellDatabase?
                .SpellDatabase;
            Spell nullUsedByProbe = null;

            try
            {
                Item gear = ItemFactory.GetRegisteredItemsForTesting()
                    .Where(item => IsCraftedEquippable(item) &&
                        !item.Relic && item.ItemValue > 0 &&
                        item.RequiredSlot != Item.SlotType.Primary &&
                        item.RequiredSlot != Item.SlotType.Secondary &&
                        item.RequiredSlot != Item.SlotType.PrimaryOrSecondary &&
                        item.RequiredSlot != Item.SlotType.Ring &&
                        item.RequiredSlot != Item.SlotType.Bracer &&
                        item.Classes != null &&
                        item.Classes.Contains(sim.MyStats.CharacterClass) &&
                        sim.MyEquipment.Any(slot => slot != null &&
                            slot.ThisSlotType == item.RequiredSlot))
                    .OrderByDescending(item => item.ItemLevel)
                    .FirstOrDefault();
                SimInvSlot targetSlot = gear == null ? null : sim.MyEquipment
                    .FirstOrDefault(slot => slot != null &&
                        slot.ThisSlotType == gear.RequiredSlot);
                if (gear == null || targetSlot == null)
                    errors.Add("no compatible crafted armor probe");
                else
                {
                    targetSlot.MyItem = GameData.PlayerInv.Empty;
                    targetSlot.Quant = 1;
                    bool accepted = sim.ReceiveNewEquippable(gear, false, "0", 1);
                    bool equipped = accepted && sim.MyEquipment.Any(slot =>
                        slot != null && ReferenceEquals(slot.MyItem, gear));
                    bool resolves = ReferenceEquals(
                        GameData.ItemDB.GetItemByID(gear.Id), gear);
                    bool contributes = simInventory?.EquippedItems != null &&
                        simInventory.EquippedItems.Contains(gear);
                    if (!equipped || !resolves || !contributes)
                        errors.Add($"native equip probe failed for {gear.ItemName} " +
                            $"(accepted={accepted}, equipped={equipped}, " +
                            $"resolves={resolves}, stats={contributes})");
                }

                Item offhand = ItemFactory
                    .GetCraftedOffhandWeaponsForTesting()
                    .Where(item => item.Classes != null &&
                        item.Classes.Contains(sim.MyStats.CharacterClass))
                    .OrderBy(item => item.ItemLevel)
                    .FirstOrDefault();
                SimInvSlot primary = sim.MyEquipment.FirstOrDefault(slot =>
                    slot != null && slot.ThisSlotType == Item.SlotType.Primary);
                SimInvSlot secondary = sim.MyEquipment.FirstOrDefault(slot =>
                    slot != null && slot.ThisSlotType == Item.SlotType.Secondary);
                if (offhand == null || primary == null || secondary == null)
                    errors.Add("no compatible crafted offhand probe");
                else
                {
                    primary.MyItem = GameData.PlayerInv.Empty;
                    primary.Quant = 1;
                    secondary.MyItem = GameData.PlayerInv.Empty;
                    secondary.Quant = 1;
                    bool accepted = sim.ReceiveNewEquippable(offhand, false,
                        "1", 1);
                    bool equipped = accepted &&
                        ReferenceEquals(secondary.MyItem, offhand);
                    bool resolves = ReferenceEquals(
                        GameData.ItemDB.GetItemByID(offhand.Id), offhand);
                    int expectedDamage = offhand.CalcDmg(offhand.WeaponDmg, 1);
                    bool attackReady = simInventory?.SimOH != null &&
                        ReferenceEquals(simInventory.SimOH.MyItem, offhand) &&
                        simInventory.OHDmg == expectedDamage &&
                        sim.GetComponent<NPC>()?.OHAtkDmg == expectedDamage;
                    if (!equipped || !resolves || !attackReady)
                        errors.Add($"native Sim offhand probe failed for " +
                            $"{offhand.ItemName} (accepted={accepted}, " +
                            $"secondary={equipped}, resolves={resolves}, " +
                            $"attack={attackReady})");
                }

                // Restore the exact gear/held state before probing consumables.
                foreach (var snapshot in equipmentSnapshot)
                    if (snapshot.Slot != null)
                    {
                        snapshot.Slot.MyItem = snapshot.Item;
                        snapshot.Slot.Quant = snapshot.Quantity;
                    }
                sim.AllHeldItems.Clear();
                sim.AllHeldItems.AddRange(heldSnapshot.Select(entry =>
                    new ItemSaveData(entry.ID, entry.Quality)));
                sim.AuditInventory();

                SimPlayerCraftedConsumableController existingController =
                    sim.GetComponent<SimPlayerCraftedConsumableController>();
                if (existingController == null)
                    errors.Add("autonomous consumable controller was not attached " +
                        "by the live SimPlayer load path");
                NPC simNpc = sim.GetComponent<NPC>();
                if (simNpc?.MyBuffSpells?.Any(
                        ItemFactory.IsConsumableSpell) == true)
                    errors.Add("crafted food remained in native infinite-use " +
                        "SimPlayer buff AI");

                // Reproduce both native null hazards responsible for the
                // loader stopping at the auction-house/finalizing boundary:
                // an injected Spell without UsedBy and an unresolved held
                // item from a loader/mod ordering race. The prefix must repair
                // the former, defer the latter, and preserve the save entry.
                nullUsedByProbe = ScriptableObject.CreateInstance<Spell>();
                nullUsedByProbe.Id = "CS_QA_NULL_USEDBY";
                nullUsedByProbe.SpellName = "QA null UsedBy";
                nullUsedByProbe.UsedBy = null;
                nullUsedByProbe.SimUsable = false;
                GameData.SpellDatabase.SpellDatabase =
                    GameData.SpellDatabase.SpellDatabase
                        .Concat(new[] { nullUsedByProbe }).ToArray();
                var unresolved = new ItemSaveData(
                    "CS_QA_UNRESOLVED_HELD_ITEM", 1);
                sim.AllHeldItems.Add(unresolved);
                sim.LoadSimSpells();
                if (nullUsedByProbe.UsedBy == null)
                    errors.Add("Sim spell loader left null UsedBy unrepaired");
                if (!sim.AllHeldItems.Contains(unresolved))
                    errors.Add("Sim spell loader lost an unresolved held item");
                sim.AllHeldItems.Remove(unresolved);

                var samples = new Dictionary<string, Item>(
                    StringComparer.OrdinalIgnoreCase);
                foreach (string category in new[] { "Food", "Drink" })
                {
                    if (HasActiveCategory(sim.MyStats, category))
                    {
                        errors.Add(category + " probe blocked by an active buff");
                        continue;
                    }
                    Item sample = ItemFactory.ConsumableBuffs
                        .Where(pair => string.Equals(pair.Value.category,
                            category, StringComparison.OrdinalIgnoreCase))
                        .Select(pair => ItemFactory.GetItem(pair.Key))
                        .Where(item => item != null &&
                            (item.Lore ?? "").IndexOf("full hp restore",
                                StringComparison.OrdinalIgnoreCase) < 0 &&
                            (item.Lore ?? "").IndexOf(
                                "extends active buffs by 50%",
                                StringComparison.OrdinalIgnoreCase) < 0)
                        .OrderBy(item => item.ItemValue)
                        .FirstOrDefault();
                    if (sample == null)
                    {
                        errors.Add("no " + category + " probe item");
                        continue;
                    }
                    samples[category] = sample;
                    SetHeldQuantity(sim.AllHeldItems, sample.Id, 0);
                    if (!ProbeConsumableTradeHandler(sim, sample, 2,
                            out string tradeDetail))
                        errors.Add(category + " trade probe failed: " +
                            tradeDetail);
                }

                SimPlayerCraftedConsumableController controller =
                    SimPlayerCraftedConsumableController.Attach(sim);
                bool foodConsumed = false;
                bool drinkConsumed = false;
                string cycleDetail = "both category probes were not available";
                bool cycled = controller != null && samples.Count == 2 &&
                    controller.RunCycleForTesting(out foodConsumed,
                        out drinkConsumed, out cycleDetail);
                if (controller == null)
                    cycleDetail = "controller unavailable";

                foreach (var pair in samples)
                {
                    string category = pair.Key;
                    Item sample = pair.Value;
                    bool consumed = string.Equals(category, "Food",
                        StringComparison.OrdinalIgnoreCase)
                        ? foodConsumed : drinkConsumed;
                    int remaining = GetHeldQuantity(sim.AllHeldItems, sample.Id);
                    StatusEffect applied = sim.MyStats.StatusEffects
                        .Take(Mathf.Min(30, sim.MyStats.StatusEffects.Length))
                        .FirstOrDefault(status => status != null &&
                            ReferenceEquals(status.Effect,
                                sample.ItemEffectOnClick));
                    if (!cycled || !consumed || remaining != 1 || applied == null)
                        errors.Add(category + " live probe failed: " +
                            cycleDetail + $"; remaining={remaining}; " +
                            $"effect={(applied == null ? "missing" : "active")}");
                    if (applied != null)
                    {
                        appliedSpells.Add(applied.Effect);
                        int index = Array.IndexOf(sim.MyStats.StatusEffects,
                            applied);
                        if (index >= 0) sim.MyStats.RemoveStatusEffect(index);
                    }
                }
            }
            catch (Exception ex)
            {
                errors.Add("live probe exception: " + ex.Message);
            }
            finally
            {
                foreach (int index in Enumerable.Range(0, Mathf.Min(30,
                    sim.MyStats.StatusEffects.Length)).Reverse())
                    if (sim.MyStats.StatusEffects[index]?.Effect != null &&
                        appliedSpells.Contains(
                            sim.MyStats.StatusEffects[index].Effect))
                        sim.MyStats.RemoveStatusEffect(index);
                foreach (var snapshot in equipmentSnapshot)
                    if (snapshot.Slot != null)
                    {
                        snapshot.Slot.MyItem = snapshot.Item;
                        snapshot.Slot.Quant = snapshot.Quantity;
                    }
                sim.AllHeldItems.Clear();
                sim.AllHeldItems.AddRange(heldSnapshot.Select(entry =>
                    new ItemSaveData(entry.ID, entry.Quality)));
                if (sim.StoredItems != null)
                {
                    sim.StoredItems.Clear();
                    if (storedSnapshot != null)
                        sim.StoredItems.AddRange(storedSnapshot);
                }
                if (GameData.SpellDatabase != null &&
                    spellDatabaseSnapshot != null)
                    GameData.SpellDatabase.SpellDatabase =
                        spellDatabaseSnapshot;
                if (nullUsedByProbe != null)
                    UnityEngine.Object.Destroy(nullUsedByProbe);
                try
                {
                    sim.AuditInventory();
                    sim.LoadSimSpells();
                    sim.SaveSim();
                }
                catch (Exception restoreError)
                {
                    errors.Add("state restore failed: " + restoreError.Message);
                }
            }

            string simName = sim.GetComponent<NPC>()?.NPCName ??
                sim.transform.name;
            detail = errors.Count == 0
                ? $"{simName}: native crafted armor stats and offhand attack " +
                  "use/save/ID resolution passed; installed trade handler and " +
                  "autonomous controller accepted Food and Drink, applied " +
                  "each once, and decremented 2->1; null UsedBy and unresolved " +
                  "held-item load hazards were repaired without data loss; " +
                  restrictionDetail +
                  "; exact pre-test Sim state was restored"
                : string.Join("; ", errors);
            return errors.Count == 0;
        }
    }

    internal sealed class SimPlayerCraftedConsumableController : MonoBehaviour
    {
        private SimPlayer _sim;
        private float _nextCheck;
        private bool _busy;

        internal static SimPlayerCraftedConsumableController Attach(
            SimPlayer sim)
        {
            if (sim == null) return null;
            SimPlayerCraftedConsumableController controller =
                sim.GetComponent<SimPlayerCraftedConsumableController>();
            if (controller == null)
                controller = sim.gameObject
                    .AddComponent<SimPlayerCraftedConsumableController>();
            controller._sim = sim;
            return controller;
        }

        private void Update()
        {
            if (_busy || _sim == null || Time.unscaledTime < _nextCheck) return;
            _nextCheck = Time.unscaledTime + 3f;
            RunConsumptionCycle(out _, out _, out _);
        }

        internal bool RunCycleForTesting(out bool foodConsumed,
            out bool drinkConsumed, out string detail)
        {
            return RunConsumptionCycle(out foodConsumed,
                out drinkConsumed, out detail);
        }

        private bool RunConsumptionCycle(out bool foodConsumed,
            out bool drinkConsumed, out string detail)
        {
            foodConsumed = false;
            drinkConsumed = false;
            detail = "controller is busy or detached";
            if (_busy || _sim == null) return false;
            _busy = true;
            try
            {
                foodConsumed = SimPlayerCraftedItems.TryConsumeNext(_sim,
                    "Food", out string foodDetail);
                drinkConsumed = SimPlayerCraftedItems.TryConsumeNext(_sim,
                    "Drink", out string drinkDetail);
                detail = "Food=" + foodDetail + "; Drink=" + drinkDetail;
                return foodConsumed || drinkConsumed;
            }
            finally
            {
                _busy = false;
            }
        }
    }

    [HarmonyPatch(typeof(SimTradeWindow), nameof(SimTradeWindow.CompleteTrade))]
    internal static class Patch_SimTradeCraftedConsumables
    {
        private static bool Prefix(SimTradeWindow __instance)
        {
            try
            {
                Item item = __instance?.LootSlot?.MyItem;
                if (SimPlayerCraftedItems.IsCraftedEquippable(item))
                    SimPlayerCraftedItems.PrepareCraftedEquipment(item);
                return !SimPlayerCraftedItems
                    .TryHandleConsumableTrade(__instance);
            }
            catch (Exception ex)
            {
                SkillsPlugin.Log.LogError(
                    "Sim crafted-item trade patch failed safely: " + ex);
                return true;
            }
        }
    }

    [HarmonyPatch(typeof(SimPlayer), nameof(SimPlayer.IsThatAnUpgrade))]
    internal static class Patch_SimPlayerCraftedUpgradeInterest
    {
        private static void Prefix(Item _item)
        {
            try
            {
                if (SimPlayerCraftedItems.IsCraftedEquippable(_item))
                    SimPlayerCraftedItems.PrepareCraftedEquipment(_item);
            }
            catch (Exception ex)
            {
                SkillsPlugin.Log.LogWarning(
                    "Sim crafted upgrade preparation failed safely: " +
                    ex.Message);
            }
        }

        private static void Postfix(SimPlayer __instance, Item _item,
            ref bool __result)
        {
            if (__result) return;
            try
            {
                __result = SimPlayerCraftedItems
                    .IsCraftedNecromancerUpgrade(__instance, _item);
            }
            catch (Exception ex)
            {
                SkillsPlugin.Log.LogWarning(
                    "Sim crafted upgrade evaluation failed safely: " +
                    ex.Message);
            }
        }
    }

    [HarmonyPatch(typeof(SimPlayer), nameof(SimPlayer.LoadSimSpells))]
    internal static class Patch_SimPlayerCraftedConsumableSpellFilter
    {
        internal sealed class LoadState
        {
            internal readonly List<ItemSaveData> DeferredHeldItems =
                new List<ItemSaveData>();
        }

        [HarmonyPrefix, HarmonyPriority(Priority.First)]
        private static void Prefix(SimPlayer __instance,
            out LoadState __state)
        {
            __state = PrepareNativeSpellLoad(__instance);
        }

        private static void Postfix(SimPlayer __instance, LoadState __state)
        {
            RestoreDeferredHeldItems(__instance, __state);
            try
            {
                SimPlayerCraftedItems
                    .RemoveConsumableSpellsFromNativeAi(__instance);
            }
            catch (Exception ex)
            {
                SkillsPlugin.Log.LogWarning(
                    "Sim consumable spell filtering failed safely: " +
                    ex.Message);
            }
        }

        [HarmonyFinalizer]
        private static Exception Finalizer(Exception __exception,
            SimPlayer __instance, LoadState __state)
        {
            RestoreDeferredHeldItems(__instance, __state);
            if (!(__exception is NullReferenceException)) return __exception;

            // Native LoadSimSpells contains several unguarded dereferences.
            // The prefix repairs the known collection, spell, equipment, and
            // unresolved held-item hazards. If a loader supplies another
            // partially initialized SimPlayer, fail that Sim's spell load
            // safely instead of aborting the login coroutine at the auction-
            // house/finalizing boundary.
            string name = __instance?.GetThisNPC()?.NPCName ??
                __instance?.name ?? "unknown SimPlayer";
            SkillsPlugin.Log?.LogWarning(
                $"SimPlayer spell load recovered for '{name}': " +
                __exception.Message);
            return null;
        }

        internal static LoadState PrepareNativeSpellLoad(SimPlayer sim)
        {
            var state = new LoadState();
            if (sim == null) return state;

            sim.Spellbook ??= new List<SimPlayerSpellSlot>();
            sim.MyAcquiredSpells ??= new List<Spell>();
            sim.AllHeldItems ??= new List<ItemSaveData>();
            sim.MyEquipment ??= new List<SimInvSlot>();
            sim.MyEquipment.RemoveAll(slot => slot == null);
            Item empty = GameData.PlayerInv?.Empty;
            if (empty != null)
            {
                foreach (SimInvSlot slot in sim.MyEquipment)
                {
                    if (slot.MyItem == null)
                    {
                        slot.MyItem = empty;
                        slot.Quant = 1;
                    }
                }
            }

            NPC npc = sim.GetThisNPC();
            if (npc != null)
            {
                npc.MyHealSpells ??= new List<Spell>();
                npc.MyBuffSpells ??= new List<Spell>();
                npc.MyAttackSpells ??= new List<Spell>();
                npc.MyCCSpells ??= new List<Spell>();
                npc.MyTauntSpell ??= new List<Spell>();
                npc.GroupHeals ??= new List<Spell>();
            }

            SpellDB database = GameData.SpellDatabase;
            if (database?.SpellDatabase != null)
            {
                database.SpellDatabase = database.SpellDatabase
                    .Where(spell => spell != null).ToArray();
                foreach (Spell spell in database.SpellDatabase)
                    spell.UsedBy ??= new List<Class>();
            }

            ItemDatabase itemDatabase = GameData.ItemDB;
            if (itemDatabase != null && sim.AllHeldItems.Count > 0)
            {
                foreach (ItemSaveData held in sim.AllHeldItems.ToArray())
                {
                    if (held == null || string.IsNullOrWhiteSpace(held.ID))
                    {
                        state.DeferredHeldItems.Add(held);
                        continue;
                    }
                    Item resolved = null;
                    try { resolved = itemDatabase.GetItemByID(held.ID); }
                    catch { }
                    if (resolved == null)
                        state.DeferredHeldItems.Add(held);
                }
                if (state.DeferredHeldItems.Count > 0)
                    sim.AllHeldItems.RemoveAll(item =>
                        state.DeferredHeldItems.Contains(item));
            }
            return state;
        }

        private static void RestoreDeferredHeldItems(SimPlayer sim,
            LoadState state)
        {
            if (sim == null || state?.DeferredHeldItems == null ||
                state.DeferredHeldItems.Count == 0) return;
            sim.AllHeldItems ??= new List<ItemSaveData>();
            foreach (ItemSaveData held in state.DeferredHeldItems)
            {
                if (held != null && !sim.AllHeldItems.Contains(held))
                    sim.AllHeldItems.Add(held);
            }
            state.DeferredHeldItems.Clear();
        }
    }
}
