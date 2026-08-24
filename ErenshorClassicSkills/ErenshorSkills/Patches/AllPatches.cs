using System;
using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;

namespace ErenshorSkills.Patches
{
    [Flags]
    internal enum LootInjectionKind
    {
        Recipe = 1,
        CreatureMaterial = 2,
        EndgameMaterial = 4,
    }

    /// <summary>
    /// LoadLootTable runs every time a corpse window is reopened. Keep each
    /// custom roll tied to the corpse so reopening cannot reroll its loot.
    /// </summary>
    internal static class LootInjectionGuard
    {
        private sealed class State { public int Mask; }
        private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<LootTable, State>
            States = new System.Runtime.CompilerServices.ConditionalWeakTable<LootTable, State>();

        public static bool TryMark(LootTable table, LootInjectionKind kind)
        {
            if (table == null) return false;
            var state = States.GetOrCreateValue(table);
            int bit = (int)kind;
            if ((state.Mask & bit) != 0) return false;
            state.Mask |= bit;
            return true;
        }
    }

    // ═══════════════════════════════════════════════════════════════════
    // CHARACTER LOAD / SAVE
    // ═══════════════════════════════════════════════════════════════════

    /// <summary>Save skill data when inventory toggles, and sync skills window visibility.</summary>
    [HarmonyPatch(typeof(Inventory), "ToggleInv")]
    public static class Patch_SaveOnInventory
    {
        public static void Postfix(Inventory __instance)
        {
            try
            {
                // Save custom items before saving skills
                Items.ItemFactory.SaveCustomInventory();
                SkillsSaveManager.Save();
                SkillsPlugin.Log.LogInfo("ToggleInv: saved skills + custom inventory.");
            }
            catch (Exception ex)
            {
                SkillsPlugin.Log.LogError($"Error saving skills: {ex.Message}");
            }

            // Sync skills window with inventory visibility
            try
            {
                bool invOpen = __instance.InvWindow != null &&
                               __instance.InvWindow.activeSelf;
                if (invOpen && !SkillsUI.IsWindowVisible)
                    SkillsUI.ToggleWindow();
                else if (!invOpen && SkillsUI.IsWindowVisible)
                    SkillsUI.ToggleWindow();
            }
            catch { }
        }
    }

    // ═══════════════════════════════════════════════════════════════════
    // FISHING PATCHES
    // ═══════════════════════════════════════════════════════════════════

    /// <summary>
    /// Award fishing XP when a catch completes.
    /// Hooks resetFishing instead of StartFishing to avoid interfering
    /// with the fishing coroutine loop.
    /// </summary>
    [HarmonyPatch(typeof(Fishing), "resetFishing")]
    public static class Patch_DoFishing
    {
        public static void Prefix(Fishing __instance, bool ___fishCaught, bool ___fishing,
            Item ___caughtItem)
        {
            if (!SkillsPlugin.CfgEnableFishing.Value) return;
            // Prefix runs BEFORE resetFishing clears fishCaught
            if (!___fishCaught) return;
            // Only award XP if still in the fishing state
            // (not if the player moved/jumped and the coroutine just cleaned up)
            if (!___fishing) return;
            try
            {
                // resetFishing still owns the exact caughtItem reference here.
                // Reading the last inventory entry was unreliable because the
                // inventory list is slot-ordered, not insertion-ordered.
                string item = ___caughtItem?.ItemName ?? "fish";
                bool isJunk = false;
                bool isRare = false;
                if (!string.IsNullOrEmpty(item))
                {
                    string lower = item.ToLowerInvariant();
                    isJunk = lower.Contains("seaweed") || lower.Contains("boot") ||
                             lower.Contains("cloth shoe") || lower.Contains("driftwood");
                    isRare = lower.Contains("moongill") || lower.Contains("stonemouth") ||
                             lower.Contains("treasure map") || lower.Contains("mold") ||
                             lower.Contains("worm eel") || lower.Contains("fanged") ||
                             lower.Contains("golden") || lower.Contains("caveskimmer");
                }

                bool eligibleForMaterials = Skills.FishingSkill.OnCatch(item, isRare, isJunk);

                // Give cooking materials based on catch type
                if (eligibleForMaterials && !isJunk)
                {
                    string specialFish = null;
                    if (isRare)
                    {
                        string lower = item.ToLowerInvariant();
                        if (lower.Contains("moongill"))
                            specialFish = "Moongill Trout";
                        else if (lower.Contains("golden"))
                            specialFish = "Golden Carp";
                        else
                            specialFish = UnityEngine.Random.value < 0.5f
                                ? "Moongill Trout" : "Golden Carp";
                    }

                    var mutation = Items.BagSystem.BeginExternalMutation();
                    try
                    {
                        if (!Items.ItemFactory.GiveItemToPlayer("Raw Fish") ||
                            (specialFish != null &&
                             !Items.ItemFactory.GiveItemToPlayer(specialFish)))
                            throw new InvalidOperationException(
                                "There is not enough inventory space for the bonus catch materials.");
                        if (!Items.BagSystem.CommitExternalMutation(
                            "fishing-materials", mutation))
                            throw new InvalidOperationException(
                                "The bonus catch materials could not be saved.");
                    }
                    catch (Exception rewardError)
                    {
                        Items.BagSystem.RollbackExternalMutation(mutation);
                        SkillsPlugin.Log.LogError(
                            $"Fishing material reward rolled back: {rewardError}");
                        ChatHelper.Send(
                            "<color=#EF5350>[Fishing]</color> Bonus cooking " +
                            "materials were not awarded; your original catch is safe.");
                    }
                }
            }
            catch (Exception ex)
            {
                SkillsPlugin.Log.LogError($"Fishing patch error: {ex.Message}");
            }
        }
    }

    // Fishing cast-time reduction removed — not implemented.
    // Safe Fall removed entirely.

    /// <summary>
    /// Fishing Catch Bonus — adds extra nibbles based on fishing skill.
    /// The game's fishing uses a 'nibble' counter: more nibbles = higher catch chance.
    /// This patch gives a skill-based chance to add bonus nibbles each fishing tick,
    /// making higher-skilled fishers catch fish more reliably.
    /// </summary>
    [HarmonyPatch(typeof(Fishing), "Update")]
    public static class Patch_FishingCatchBonus
    {
        public static void Prefix(Fishing __instance, ref int ___nibble, bool ___fishing,
            float ___waitForFish, Water ___fishWater)
        {
            if (!SkillsPlugin.CfgEnableFishing.Value) return;
            if (!___fishing || ___fishWater == null) return;
            // Only boost when the timer is about to expire (catch check imminent)
            if (___waitForFish > 60f * Time.deltaTime * 2f) return;
            try
            {
                float bonus = Skills.FishingSkill.CatchBonus;
                if (bonus <= 0f) return;
                // RareCatchBonus is skill * 0.5, so at skill 50 = 25%.
                // Roll for a bonus nibble: bonus% chance to add +1 nibble
                if (UnityEngine.Random.value * 100f < bonus)
                {
                    ___nibble++;
                }
            }
            catch { }
        }
    }

    // Rare catch bonus is now handled by Patch_FishingRareCatch above

    // ═══════════════════════════════════════════════════════════════════
    // SAFE FALL PATCH
    // ═══════════════════════════════════════════════════════════════════

    /// <summary>
    /// Intercept fall damage to apply Safe Fall reduction.
    /// Erenshor applies fall damage through the character's damage system.
    /// </summary>
    // Safe Fall removed

    // ═══════════════════════════════════════════════════════════════════
    // SWIMMING PATCH
    // ═══════════════════════════════════════════════════════════════════

    /// <summary>
    /// Detect when the player is in water and award swimming XP.
    /// PlayerControl has a "Swimming" bool field and WaterMovement method.
    /// </summary>
    [HarmonyPatch(typeof(PlayerControl), "WaterMovement")]
    public static class Patch_Swimming
    {
        private static float _lastCheck = 0f;

        public static void Postfix(PlayerControl __instance, bool ___Swimming)
        {
            if (!SkillsPlugin.CfgEnableSwimming.Value) return;
            if (Time.time - _lastCheck < 1f) return;
            _lastCheck = Time.time;

            try
            {
                bool inWater = ___Swimming;
                var cc = __instance.GetComponent<CharacterController>();
                bool moving = cc != null && cc.velocity.magnitude > 0.1f;

                Skills.SwimmingSkill.OnSwimTick(inWater, moving);
            }
            catch { }
        }
    }

    // ═══════════════════════════════════════════════════════════════════
    // MEDITATION TICK (via PlayerMovement Update)
    // ═══════════════════════════════════════════════════════════════════

    /// <summary>
    /// Process meditation ticks. Piggybacks on the movement update
    /// to check meditation state every frame.
    /// </summary>
    [HarmonyPatch(typeof(PlayerControl), "LateUpdate")]
    public static class Patch_MeditationTick
    {
        public static void Postfix()
        {
            if (!SkillsPlugin.CfgEnableMeditate.Value) return;
            try
            {
                Skills.MeditateSkill.ProcessTick();
            }
            catch { }
        }
    }

    // ═══════════════════════════════════════════════════════════════════
    // SWIM SPEED BONUS
    // ═══════════════════════════════════════════════════════════════════

    /// <summary>
    /// Apply swimming speed bonus based on Swimming skill level.
    /// Reads the game's base swim speed field and caps actualSpeed
    /// to prevent exponential compounding.
    /// </summary>
    [HarmonyPatch(typeof(PlayerControl), "WaterMovement")]
    public static class Patch_SwimSpeed
    {
        public static void Postfix(PlayerControl __instance, bool ___Swimming, float ___speed)
        {
            if (!SkillsPlugin.CfgEnableSwimming.Value) return;
            if (!___Swimming) return;
            try
            {
                float mult = Skills.SwimmingSkill.SpeedMultiplier;
                if (mult <= 1f) return;

                // ___speed is the base movement speed (private field, typically 3f)
                // Cap actualSpeed to base * multiplier to prevent compounding
                float maxSpeed = ___speed * mult;
                if (__instance.actualSpeed > 0.1f && __instance.actualSpeed < maxSpeed)
                    __instance.actualSpeed = Mathf.Min(__instance.actualSpeed * mult, maxSpeed);
            }
            catch { }
        }
    }

    // ═══════════════════════════════════════════════════════════════════
    // BUFF DURATION BONUS (Food Tolerance)
    // ═══════════════════════════════════════════════════════════════════

    /// <summary>
    /// Extend the direct status effect used by Classic Skills food and drink.
    /// Native spell buffs use AddStatusEffect and are handled separately.
    /// </summary>
    [HarmonyPatch(typeof(Stats), "AddStatusEffectNoChecks")]
    public static class Patch_BuffDuration
    {
        public static void Postfix(Stats __instance, Spell spell, bool _fromPlayer)
        {
            if (!_fromPlayer || !SkillsSaveManager.HasLoaded) return;
            try
            {
                if (!SkillsPlugin.CfgEnableFoodTolerance.Value ||
                    !Items.ItemFactory.IsConsumableSpell(spell)) return;
                float multiplier = 1f + Skills.FoodToleranceSkill.DurationBonus / 100f;
                if (multiplier <= 1f || __instance?.StatusEffects == null) return;

                // Find the status effect that was just applied and extend it
                for (int i = 0; i < __instance.StatusEffects.Length; i++)
                {
                    if (__instance.StatusEffects[i] != null &&
                        __instance.StatusEffects[i].Effect == spell)
                    {
                        __instance.StatusEffects[i].Duration =
                            Mathf.RoundToInt(__instance.StatusEffects[i].Duration * multiplier);
                        break;
                    }
                }
            }
            catch (Exception ex)
            {
                SkillsPlugin.Log.LogWarning($"Food duration adjustment failed: {ex.Message}");
            }
        }
    }

    // ═══════════════════════════════════════════════════════════════════
    // CUSTOM CONSUMABLE HANDLING
    // ═══════════════════════════════════════════════════════════════════

    /// <summary>
    /// Intercept UseConsumable to handle our custom food items.
    /// The game's native path crashes because our runtime Spell objects
    /// lack fields that CastSpell.StartSpell expects. We apply the buff
    /// directly via Stats.AddStatusEffect instead.
    /// </summary>
    /// <summary>
    /// Intercept UseConsumable to handle our custom food items.
    /// We apply the buff directly and handle item removal using
    /// the game's own pattern (Quantity--, set to Empty, UpdateSlotImage).
    /// </summary>
    [HarmonyPatch(typeof(ItemIcon), "UseConsumable")]
    public static class Patch_UseCustomConsumable
    {
        private sealed class EffectSnapshot
        {
            public int Index;
            public Spell Effect;
            public float Duration;
            public bool FromPlayer;
            public int BonusDamage;
            public bool CastedByPC;
            public Character Owner;
            public Character CreditDPS;
        }

        private sealed class DurationSnapshot
        {
            public StatusEffect Slot;
            public Spell Effect;
            public float Duration;
        }

        private static void RestoreEffects(Stats stats, List<EffectSnapshot> snapshots)
        {
            if (stats?.StatusEffects == null || snapshots == null) return;
            foreach (var saved in snapshots)
            {
                if (saved == null || saved.Index < 0 ||
                    saved.Index >= stats.StatusEffects.Length ||
                    stats.StatusEffects[saved.Index] == null) continue;
                var slot = stats.StatusEffects[saved.Index];
                slot.Effect = saved.Effect;
                slot.Duration = saved.Duration;
                slot.fromPlayer = saved.FromPlayer;
                slot.bonusDmg = saved.BonusDamage;
                slot.CastedByPC = saved.CastedByPC;
                slot.Owner = saved.Owner;
                slot.CreditDPS = saved.CreditDPS;
            }
            stats.CalcStats();
        }

        private static void ExtendActiveBuffs(Stats stats, Spell newlyApplied,
            List<DurationSnapshot> snapshots)
        {
            if (stats?.StatusEffects == null || snapshots == null) return;
            foreach (var status in stats.StatusEffects)
            {
                var effect = status?.Effect;
                if (effect == null || ReferenceEquals(effect, newlyApplied) ||
                    effect.WornEffect || effect.Type != Spell.SpellType.Beneficial ||
                    status.Duration <= 0f) continue;
                snapshots.Add(new DurationSnapshot
                {
                    Slot = status,
                    Effect = effect,
                    Duration = status.Duration
                });
                status.Duration = Mathf.Min(status.Duration * 1.5f, 1000000f);
            }
        }

        private static void RestoreDurations(List<DurationSnapshot> snapshots)
        {
            if (snapshots == null) return;
            foreach (var saved in snapshots)
                if (saved?.Slot != null && ReferenceEquals(saved.Slot.Effect, saved.Effect))
                    saved.Slot.Duration = saved.Duration;
        }

        public static bool Prefix(ItemIcon __instance)
        {
            try
            {
                var item = __instance.MyItem;
                if (item == null) return true;

                string name = item.ItemName;
                if (string.IsNullOrEmpty(name)) return true;

                // Recipe scrolls — right-click to learn the recipe
                if (Stations.RecipeVendors.IsRecipeScroll(item))
                {
                    Stations.RecipeVendors.TryConsumeRecipeScroll(__instance);
                    return false; // Don't pass to game's UseConsumable
                }

                // If a bag window is open, right-clicking an item stores it in the bag
                if (Items.BagWindow.AnyOpen && !Items.BagSystem.IsBag(name))
                {
                    bool cursorBusy = GameData.ItemOnCursor != null ||
                        (GameData.MouseSlot != null && GameData.MouseSlot.dragging);
                    if (!cursorBusy && Items.BagSystem.TryStoreFromInventorySlot(__instance))
                        return false; // Consumed the click
                }

                // Check if this is a bag — open bag window instead of consuming
                if (Items.BagSystem.IsBag(name))
                {
                    string bagId = Items.BagSystem.EnsureBagIdentity(__instance);
                    if (string.IsNullOrEmpty(bagId))
                    {
                        ChatHelper.Send(
                            "<color=#EF5350>[Bag]</color> The bag could not be opened " +
                            "because its identity could not be saved safely.");
                        return false;
                    }
                    Items.BagWindow.Open(bagId, name);
                    return false; // Don't consume the bag!
                }

                if (!Items.ItemFactory.ConsumableBuffs.TryGetValue(name, out var buff))
                    return true; // not ours, let game handle it

                if (!Items.BagSystem.IsDirectInventorySlot(__instance))
                {
                    ChatHelper.Send(
                        "<color=#EF5350>[Consumable]</color> Move the item into " +
                        "your character inventory before using it.");
                    return false;
                }

                // Check if player is alive and not casting
                if (!GameData.PlayerControl.Myself.Alive) return false;
                if (GameData.PlayerControl.Myself.MySpells.isCasting()) return false;

                // Apply the buff via AddStatusEffectNoChecks
                var stats = GameData.PlayerStats;
                if (stats == null) return false;

                // CATEGORY CHECK: Food overwrites Food and Drink overwrites Drink.
                // StatusEffects is a fixed array of reusable StatusEffect objects;
                // assigning an array element to null permanently destroys capacity.
                string category = buff.category; // "Food" or "Drink"
                string catTag = $"[CS_{category}]";
                var categorySlots = new List<int>();
                int freeSlot = -1;
                int statusSlotCount = stats.StatusEffects == null
                    ? 0 : Mathf.Min(30, stats.StatusEffects.Length);
                for (int i = 0; i < statusSlotCount; i++)
                {
                    var eff = stats.StatusEffects[i];
                    if (eff == null) continue;
                    if (eff.Effect == null)
                    {
                        if (freeSlot < 0) freeSlot = i;
                        continue;
                    }
                    if (!string.IsNullOrEmpty(eff.Effect.SpellName) &&
                        eff.Effect.SpellName.Contains(catTag))
                        categorySlots.Add(i);
                }

                if (categorySlots.Count == 0 && freeSlot < 0)
                {
                    ChatHelper.Send(
                        $"<color=#EF5350>[{category}]</color> You have no free status-effect slot. " +
                        $"The {name} was not consumed.");
                    return false;
                }

                var mutation = Items.BagSystem.BeginExternalMutation();
                int healthBefore = stats.CurrentHP;
                var spell = ScriptableObject.CreateInstance<Spell>();
                var template = item.ItemEffectOnClick;
                // Embed category tag in spell name for later removal
                // Persist the deterministic template ID. SpellDB is patched to
                // resolve that template when Erenshor reloads status effects.
                spell.Id = !string.IsNullOrEmpty(template?.Id)
                    ? template.Id : $"CS_Buff_{name.Replace(" ", "_")}";
                spell.SpellName = $"{name} {catTag}";
                spell.SpellDesc = template?.SpellDesc ?? $"+{buff.bonus} {buff.stat}";
                spell.StatusEffectMessageOnPlayer = "You feel nourished.";
                spell.SpellDurationInTicks = template != null
                    ? template.SpellDurationInTicks
                    : Mathf.Max(1, Mathf.RoundToInt(buff.dur / 3f));
                spell.SelfOnly = true;
                spell.InflictOnSelf = true;
                spell.InstantEffect = false;
                spell.ManaCost = 0;
                spell.SpellChargeTime = 0f;

                // Set Type = Beneficial, Line = Global_Buff
                try
                {
                    var tf = typeof(Spell).GetField("Type");
                    if (tf != null)
                        tf.SetValue(spell, System.Enum.ToObject(tf.FieldType, 2));
                    var lf = typeof(Spell).GetField("Line");
                    if (lf != null)
                        lf.SetValue(spell, System.Enum.ToObject(lf.FieldType, 29));
                }
                catch { }

                if (template != null)
                {
                    spell.Str = template.Str; spell.End = template.End;
                    spell.Dex = template.Dex; spell.Agi = template.Agi;
                    spell.Int = template.Int; spell.Wis = template.Wis;
                    spell.Cha = template.Cha; spell.HP = template.HP;
                    spell.Mana = template.Mana; spell.AC = template.AC;
                    spell.MR = template.MR; spell.ER = template.ER;
                    spell.PR = template.PR; spell.VR = template.VR;
                    spell.TargetHealing = template.TargetHealing;
                    spell.Haste = template.Haste;
                    spell.MyDamageType = template.MyDamageType;
                }
                else
                {
                    spell.HP = buff.bonus * 5;
                    switch (buff.stat)
                    {
                        case "All Stats":
                            spell.Str = spell.End = spell.Dex = spell.Agi =
                                spell.Int = spell.Wis = spell.Cha = buff.bonus;
                            break;
                        case "Strength":     spell.Str = buff.bonus; break;
                        case "Agility":      spell.Agi = buff.bonus; break;
                        case "Endurance":    spell.End = buff.bonus; break;
                        case "Intelligence": spell.Int = buff.bonus; break;
                        case "Wisdom":       spell.Wis = buff.bonus; break;
                        case "Charisma":     spell.Cha = buff.bonus; break;
                        case "Dexterity":    spell.Dex = buff.bonus; break;
                    }
                }

                // Use the item's icon for the buff icon
                spell.SpellIcon = item.ItemIcon;

                var replacedEffects = new List<EffectSnapshot>();
                foreach (int index in categorySlots)
                {
                    var old = stats.StatusEffects[index];
                    replacedEffects.Add(new EffectSnapshot
                    {
                        Index = index,
                        Effect = old.Effect,
                        Duration = old.Duration,
                        FromPlayer = old.fromPlayer,
                        BonusDamage = old.bonusDmg,
                        CastedByPC = old.CastedByPC,
                        Owner = old.Owner,
                        CreditDPS = old.CreditDPS
                    });
                }

                try
                {
                    foreach (int index in categorySlots)
                    {
                        SkillsPlugin.Log.LogInfo(
                            $"Replacing previous {category} buff: " +
                            stats.StatusEffects[index].Effect.SpellName);
                        stats.RemoveStatusEffect(index);
                    }
                    stats.AddStatusEffectNoChecks(spell, true, 0, null);
                }
                catch (Exception ex)
                {
                    RestoreEffects(stats, replacedEffects);
                    UnityEngine.Object.Destroy(spell);
                    SkillsPlugin.Log.LogError($"Could not apply {category} buff: {ex}");
                    ChatHelper.Send(
                        $"<color=#EF5350>[{category}]</color> The buff could not be applied. " +
                        $"The {name} was not consumed.");
                    return false;
                }

                StatusEffect applied = null;
                for (int i = 0; i < statusSlotCount; i++)
                {
                    var candidate = stats.StatusEffects[i];
                    if (candidate != null && ReferenceEquals(candidate.Effect, spell))
                    {
                        applied = candidate;
                        break;
                    }
                }
                if (applied == null)
                {
                    RestoreEffects(stats, replacedEffects);
                    UnityEngine.Object.Destroy(spell);
                    ChatHelper.Send(
                        $"<color=#EF5350>[{category}]</color> The buff did not take hold. " +
                        $"The {name} was not consumed.");
                    return false;
                }

                var extendedDurations = new List<DurationSnapshot>();
                try
                {
                    if (!string.IsNullOrEmpty(item.Lore) &&
                        item.Lore.IndexOf("extends active buffs by 50%",
                            StringComparison.OrdinalIgnoreCase) >= 0)
                        ExtendActiveBuffs(stats, spell, extendedDurations);

                    if (!string.IsNullOrEmpty(item.Lore) &&
                        item.Lore.IndexOf("full hp restore",
                            StringComparison.OrdinalIgnoreCase) >= 0)
                        stats.CurrentHP = stats.CurrentMaxHP;

                    // Remove item using the game's own pattern, then make the
                    // item removal and native inventory save one transaction.
                    if (item.Disposable)
                        __instance.Quantity--;
                    if (__instance.Quantity <= 0)
                    {
                        __instance.MyItem = GameData.PlayerInv.Empty;
                        __instance.Quantity = 1;
                    }
                    __instance.UpdateSlotImage();

                    if (!Items.BagSystem.CommitExternalMutation(
                        $"consume-{category.ToLowerInvariant()}", mutation))
                        throw new InvalidOperationException(
                            "The inventory transaction could not be saved.");

                    foreach (var oldEffect in replacedEffects)
                        if (oldEffect?.Effect != null)
                            UnityEngine.Object.Destroy(oldEffect.Effect);

                    ChatHelper.Send(
                        $"<color=#CDDC39>[{category}]</color> " +
                        $"You consume the {name}. " +
                        $"<color=#AAAAAA>{spell.SpellDesc}</color>");
                }
                catch (Exception transactionError)
                {
                    for (int i = 0; i < statusSlotCount; i++)
                    {
                        var candidate = stats.StatusEffects[i];
                        if (candidate != null && ReferenceEquals(candidate.Effect, spell))
                        {
                            stats.RemoveStatusEffect(i);
                            break;
                        }
                    }
                    RestoreDurations(extendedDurations);
                    RestoreEffects(stats, replacedEffects);
                    stats.CurrentHP = healthBefore;
                    Items.BagSystem.RollbackExternalMutation(mutation);
                    UnityEngine.Object.Destroy(spell);
                    SkillsPlugin.Log.LogError(
                        $"Consumable transaction rolled back: {transactionError}");
                    ChatHelper.Send(
                        $"<color=#EF5350>[{category}]</color> The {name} was not " +
                        $"consumed because the inventory save failed.");
                    return false;
                }

                return false; // skip game's UseConsumable
            }
            catch (System.Exception ex)
            {
                SkillsPlugin.Log.LogError($"Custom consumable error: {ex}");
                return false;
            }
        }
    }

    // ═══════════════════════════════════════════════════════════════════
    // COMBAT WEAPON SKILL PATCHES
    // ═══════════════════════════════════════════════════════════════════

    // Combat weapon XP patches are now in AttackSyncFix.cs (merged with Attack Sync Fix)

    // ═══════════════════════════════════════════════════════════════════
    // RECIPE DROPS FROM ENEMIES
    // ═══════════════════════════════════════════════════════════════════

    /// <summary>
    /// Roll for recipe scroll drops when the player earns XP (enemy kill).
    /// Drop chance scales with XP earned (proxy for enemy level).
    /// Higher tier recipes drop from higher level enemies.
    /// Boss-only recipes require 200+ XP kills (named/boss mobs).
    /// Non-boss-only drops cap at trivial 178 to avoid redundancy with endgame.
    /// </summary>
    /// <summary>
    /// Inject recipe scrolls into mob loot tables before the loot window opens.
    /// Hooks LootTable.LoadLootTable to add recipe scrolls to ActualDrops.
    /// </summary>
    [HarmonyPatch(typeof(LootTable), "LoadLootTable")]
    public static class Patch_RecipeDrop
    {
        public static void Prefix(LootTable __instance)
        {
            if (!SkillsSaveManager.HasLoaded) return;
            if (!LootInjectionGuard.TryMark(__instance, LootInjectionKind.Recipe)) return;

            try
            {
                // Get mob level and boss status from the LootTable's Character
                var character = __instance.GetComponent<Character>();
                if (character == null) return;

                int mobLevel = 0;
                bool isBoss = false;
                try
                {
                    mobLevel = character.MyStats?.Level ?? 0;
                    isBoss = character.BossXp > 1f;
                }
                catch { }
                if (mobLevel <= 0) return;

                // Drop chance: 5% normal, 30% boss
                float dropChance = isBoss ? 0.30f : 0.05f;
                if (UnityEngine.Random.value > dropChance) return;

                // Mob level 1-35 maps to recipe MinSkillLevel 1-180
                int centerSkill = mobLevel * 5;
                int minRecipeSkill = Mathf.Max(1, centerSkill - 15);
                int maxRecipeSkill = Mathf.Min(180, centerSkill + 10);

                if (isBoss)
                {
                    minRecipeSkill = Mathf.Max(1, centerSkill - 5);
                    maxRecipeSkill = Mathf.Min(180, centerSkill + 20);
                }

                // Gather eligible recipes
                var candidates = new System.Collections.Generic.List<string>();
                string[] skills = { "Smithing", "Baking", "Brewing", "Fletching", "Jewelcraft", "Tailoring" };
                foreach (var ts in skills)
                {
                    var recipes = Skills.Tradeskills.GetRecipesForSkillPublic(ts);
                    foreach (var r in recipes)
                    {
                        if (r.MinSkillLevel < minRecipeSkill) continue;
                        if (r.MinSkillLevel > maxRecipeSkill) continue;
                        if (r.BossOnly && !isBoss) continue;
                        candidates.Add(r.Name);
                    }
                }

                if (candidates.Count == 0) return;

                string recipeName = candidates[UnityEngine.Random.Range(0, candidates.Count)];
                var scroll = Stations.RecipeVendors.GetScrollForRecipe(recipeName);
                if (scroll == null) return;

                // Inject into ActualDrops (max 8 loot slots)
                if (__instance.ActualDrops.Count < 8)
                {
                    __instance.ActualDrops.Add(scroll);
                }
                else
                {
                    // Replace an empty slot if all 8 are taken
                    for (int i = 0; i < __instance.ActualDrops.Count; i++)
                    {
                        if (__instance.ActualDrops[i] == null ||
                            __instance.ActualDrops[i] == GameData.PlayerInv.Empty)
                        {
                            __instance.ActualDrops[i] = scroll;
                            break;
                        }
                    }
                }
            }
            catch { }
        }
    }

    // ═══════════════════════════════════════════════════════════════════
    // MEAT DROPS FROM BEAST MOBS
    // ═══════════════════════════════════════════════════════════════════

    /// <summary>
    /// Drop Beast Meat from beast-type mobs and Spider Silk from spiders.
    /// Hooks LootTable.LoadLootTable to inject items into mob loot.
    /// Spider silk tier scales with mob level:
    ///   Low level spiders: Spider Silk Strand
    ///   Mid level spiders: Spider Silk Strand + chance of Abyssal Silk Thread
    ///   High level spiders: Abyssal Silk Thread + chance of Celestial Silk Bolt
    /// </summary>
    [HarmonyPatch(typeof(LootTable), "LoadLootTable")]
    public static class Patch_MeatDrop
    {
        public static void Prefix(LootTable __instance)
        {
            if (!SkillsSaveManager.HasLoaded) return;
            if (!LootInjectionGuard.TryMark(__instance, LootInjectionKind.CreatureMaterial)) return;
            try
            {
                var character = __instance.GetComponent<Character>();
                if (character == null) return;

                string mobName = "";
                int mobLevel = 0;
                try
                {
                    mobName = character.MyStats?.MyName ?? "";
                    mobLevel = character.MyStats?.Level ?? 0;
                }
                catch { }
                if (string.IsNullOrEmpty(mobName)) return;
                string lower = mobName.ToLowerInvariant();

                // ── Spider silk drops ──
                bool isSpider = lower.Contains("spider") || lower.Contains("arachn");
                if (isSpider)
                {
                    // 35% chance to drop silk
                    if (UnityEngine.Random.value <= 0.35f)
                    {
                        string silkName;
                        if (mobLevel >= 25)
                            silkName = UnityEngine.Random.value < 0.20f
                                ? "Celestial Silk Bolt" : "Abyssal Silk Thread";
                        else if (mobLevel >= 15)
                            silkName = UnityEngine.Random.value < 0.25f
                                ? "Abyssal Silk Thread" : "Spider Silk Strand";
                        else
                            silkName = "Spider Silk Strand";

                        var silk = Items.ItemFactory.GetItem(silkName);
                        if (silk != null && __instance.ActualDrops.Count < 8)
                            __instance.ActualDrops.Add(silk);
                    }
                }

                // ── Wyrm/Drake/Dragon drops: Beast Bone Shard + Elder Wyrm Bone ──
                bool isWyrm = lower.Contains("wyrm") || lower.Contains("drake") ||
                    lower.Contains("dragon") || lower.Contains("firebone") ||
                    lower.Contains("icebone");
                if (isWyrm)
                {
                    // 30% Beast Bone Shard
                    if (UnityEngine.Random.value <= 0.30f)
                    {
                        var bone = Items.ItemFactory.GetItem("Beast Bone Shard");
                        if (bone != null && __instance.ActualDrops.Count < 8)
                            __instance.ActualDrops.Add(bone);
                    }
                    // Elder Wyrm Bone: 15% from low level, 30% from high level
                    float wyrmBoneChance = mobLevel >= 20 ? 0.30f : 0.15f;
                    if (UnityEngine.Random.value <= wyrmBoneChance)
                    {
                        var elderBone = Items.ItemFactory.GetItem("Elder Wyrm Bone");
                        if (elderBone != null && __instance.ActualDrops.Count < 8)
                            __instance.ActualDrops.Add(elderBone);
                    }
                }

                // ── Fire creature drops: Phoenix Feather ──
                bool isFire = lower.Contains("firebone") || lower.Contains("fire") &&
                    (lower.Contains("guardian") || lower.Contains("elemental") ||
                     lower.Contains("phoenix"));
                if (isFire && mobLevel >= 15)
                {
                    if (UnityEngine.Random.value <= 0.20f)
                    {
                        var feather = Items.ItemFactory.GetItem("Phoenix Feather");
                        if (feather != null && __instance.ActualDrops.Count < 8)
                            __instance.ActualDrops.Add(feather);
                    }
                }

                // ── Void/Undead/Corrupted drops: Breath of the Abyss ──
                bool isVoid = lower.Contains("void") || lower.Contains("abyssal") ||
                    lower.Contains("blight") || lower.Contains("corrupted") ||
                    lower.Contains("sivakayan") || lower.Contains("ghost") ||
                    lower.Contains("wraith") || lower.Contains("shade") ||
                    lower.Contains("lich") || lower.Contains("undead");
                if (isVoid && mobLevel >= 15)
                {
                    if (UnityEngine.Random.value <= 0.20f)
                    {
                        var breath = Items.ItemFactory.GetItem("Breath of the Abyss");
                        if (breath != null && __instance.ActualDrops.Count < 8)
                            __instance.ActualDrops.Add(breath);
                    }
                }

                // ── Malaroth drops: Malaroth Scale Fragment ──
                bool isMalaroth = lower.Contains("malaroth");
                if (isMalaroth)
                {
                    if (UnityEngine.Random.value <= 0.35f)
                    {
                        var scale = Items.ItemFactory.GetItem("Malaroth Scale Fragment");
                        if (scale != null && __instance.ActualDrops.Count < 8)
                            __instance.ActualDrops.Add(scale);
                    }
                }

                // ── Ice/Frost creature drops: Frostweave Strand ──
                bool isFrost = lower.Contains("frost") || lower.Contains("ice") ||
                    lower.Contains("icebone") ||
                    (lower.Contains("guardian") && lower.Contains("ice"));
                if (isFrost && mobLevel >= 15)
                {
                    if (UnityEngine.Random.value <= 0.25f)
                    {
                        var frost = Items.ItemFactory.GetItem("Frostweave Strand");
                        if (frost != null && __instance.ActualDrops.Count < 8)
                            __instance.ActualDrops.Add(frost);
                    }
                }

                // ── Giant/Titan drops: Titan's Knucklebone ──
                bool isGiant = lower.Contains("giant") || lower.Contains("titan") ||
                    lower.Contains("golem") || lower.Contains("volcanic");
                if (isGiant && mobLevel >= 20)
                {
                    if (UnityEngine.Random.value <= 0.15f)
                    {
                        var titan = Items.ItemFactory.GetItem("Titan's Knucklebone");
                        if (titan != null && __instance.ActualDrops.Count < 8)
                            __instance.ActualDrops.Add(titan);
                    }
                }

                // ── Skeleton drops: Beast Bone Shard ──
                bool isSkeleton = lower.Contains("skeleton") || lower.Contains("boneraiser") ||
                    lower.Contains("boneweaver") || lower.Contains("bonesaw");
                if (isSkeleton)
                {
                    if (UnityEngine.Random.value <= 0.25f)
                    {
                        var bone = Items.ItemFactory.GetItem("Beast Bone Shard");
                        if (bone != null && __instance.ActualDrops.Count < 8)
                            __instance.ActualDrops.Add(bone);
                    }
                }

                // ── Beast meat drops ──
                bool isBeast = lower.Contains("wolf") || lower.Contains("bear") ||
                    lower.Contains("boar") || lower.Contains("beast") ||
                    lower.Contains("lion") || lower.Contains("stag") ||
                    lower.Contains("deer") || isSpider ||
                    lower.Contains("wyrm") || lower.Contains("drake") ||
                    lower.Contains("dragon") || lower.Contains("malaroth") ||
                    lower.Contains("bat") || lower.Contains("rat") ||
                    lower.Contains("hound") || lower.Contains("raptor") ||
                    lower.Contains("serpent") || lower.Contains("snake") ||
                    lower.Contains("crab") || lower.Contains("scorpion") ||
                    lower.Contains("beetle") || lower.Contains("cat") ||
                    lower.Contains("tiger") || lower.Contains("panther") ||
                    lower.Contains("gorilla") || lower.Contains("ape") ||
                    lower.Contains("lizard") || lower.Contains("croc");

                if (!isBeast) return;

                // 25% chance to drop meat
                if (UnityEngine.Random.value <= 0.25f)
                {
                    var meat = Items.ItemFactory.GetItem("Beast Meat");
                    if (meat != null && __instance.ActualDrops.Count < 8)
                        __instance.ActualDrops.Add(meat);
                }

                // 20% chance to drop hide/leather (tier based on mob level)
                if (UnityEngine.Random.value <= 0.20f)
                {
                    string hideName;
                    if (mobLevel >= 18)
                        hideName = "Tough Hide Strip";
                    else
                        hideName = "Worn Leather Scrap";

                    var hide = Items.ItemFactory.GetItem(hideName);
                    if (hide != null && __instance.ActualDrops.Count < 8)
                        __instance.ActualDrops.Add(hide);
                }

                // Endgame materials are rolled once by Patch_EndgameMaterialDrop.
                // Keeping the roll out of this beast-material patch prevents
                // wyrm/dragon bosses from receiving a second overlapping roll.
            }
            catch { }
        }
    }

    // ═══════════════════════════════════════════════════════════════════
    // MINING NODE DROPS — Custom ores, gems, and metals from mining
    // ═══════════════════════════════════════════════════════════════════

    [HarmonyPatch(typeof(MiningNode), "Mine")]
    public static class Patch_MiningNodeDrop
    {
        public static void Postfix(MiningNode __instance, Item __result)
        {
            if (!SkillsSaveManager.HasLoaded) return;
            try
            {
                string scene = UnityEngine.SceneManagement.SceneManager.GetActiveScene().name;
                int tier = GetZoneTier(scene);
                if (tier <= 0) return;

                // Check player level — must be appropriate level for the zone tier
                int playerLevel = 1;
                try { playerLevel = Mathf.Max(1, GameData.PlayerStats?.Level ?? 1); }
                catch { }
                int requiredLevel = GetRequiredLevel(tier);
                if (playerLevel < requiredLevel)
                {
                    // Player too low level — only drop T1 materials regardless of zone
                    tier = 1;
                }

                if (UnityEngine.Random.value > 0.60f) return;
                string drop = RollMiningDrop(tier);
                if (string.IsNullOrEmpty(drop)) return;
                var item = Items.ItemFactory.GetItem(drop);
                if (item == null) return;
                var inv = GameData.PlayerInv;
                if (inv == null || !Items.ItemFactory.CanAddItemToPlayer(item, 1))
                {
                    ChatHelper.Send("<color=#EF5350>[Mining]</color> Your inventory is full; the bonus find was left behind.");
                    return;
                }

                var mutation = Items.BagSystem.BeginExternalMutation();
                try
                {
                    if (!inv.AddItemToInv(item, 1) ||
                        !Items.BagSystem.CommitExternalMutation(
                            "mining-bonus", mutation))
                        throw new InvalidOperationException(
                            "The bonus mining item could not be saved.");
                    ChatHelper.Send($"<color=#FFD700>[Mining]</color> You also find: <color=#FFFFFF>{drop}</color>");
                }
                catch (Exception rewardError)
                {
                    Items.BagSystem.RollbackExternalMutation(mutation);
                    SkillsPlugin.Log.LogError(
                        $"Mining bonus reward rolled back: {rewardError}");
                    ChatHelper.Send(
                        "<color=#EF5350>[Mining]</color> The bonus find was not " +
                        "awarded because it could not be saved safely.");
                }
            }
            catch (Exception ex)
            {
                SkillsPlugin.Log.LogWarning($"Mining bonus patch failed safely: {ex.Message}");
            }
        }

        /// <summary>
        /// Minimum player level required for each mining tier.
        /// Prevents low-level players from farming high-tier materials.
        /// </summary>
        private static int GetRequiredLevel(int tier)
        {
            switch (tier)
            {
                case 1: return 1;   // Stowaway — anyone
                case 2: return 5;   // Low-mid zones
                case 3: return 12;  // Mid zones (Azure, etc.)
                case 4: return 18;  // Mid-high zones
                case 5: return 25;  // High zones
                case 6: return 30;  // Endgame zones
                default: return 1;
            }
        }

        private static int GetZoneTier(string scene)
        {
            switch (scene)
            {
                case "Stowaway": case "Tutorial": return 1;
                case "FernallaField": case "Hidden": case "Brake": return 2;
                case "Azure": case "Windwashed": case "Silkengrass": return 3;
                case "Braxonian": case "Elderstone": case "Malaroth": return 4;
                case "Soluna": case "Ripper": case "Bonepits":
                case "Vitheo": case "VitheosEnd": case "SaltedStrand": return 5;
                case "Abyssal": case "PrielPlateau": case "Azynthi":
                case "AzynthiClear": case "ShiveringStep":
                case "Loomingwood": case "Rottenfoot": case "Krakengard": return 6;
                default: return 2;
            }
        }

        private static string RollMiningDrop(int tier)
        {
            float roll = UnityEngine.Random.value;
            if (roll < 0.50f) return RollCommonOre(tier);
            if (roll < 0.75f) return RollRareOreOrGem(tier);
            if (roll < 0.90f) return RollRefinedMetal(tier);
            return RollEndgame(tier);
        }

        private static string RollCommonOre(int tier)
        {
            switch (tier)
            {
                case 1: return "Chunk of Copper Ore";
                case 2: return UnityEngine.Random.value < 0.5f ? "Chunk of Copper Ore" : "Chunk of Iron Ore";
                case 3: return UnityEngine.Random.value < 0.5f ? "Chunk of Iron Ore" : "Coal";
                case 4: return UnityEngine.Random.value < 0.5f ? "Chunk of Silver Ore" : "Chunk of Iron Ore";
                case 5: return UnityEngine.Random.value < 0.5f ? "Chunk of Adamantite Ore" : "Chunk of Platinum Ore";
                case 6: return UnityEngine.Random.value < 0.5f ? "Chunk of Adamantite Ore" : "Chunk of Gold Ore";
                default: return "Chunk of Copper Ore";
            }
        }

        private static string RollRareOreOrGem(int tier)
        {
            string[][] pools = {
                new[] { "Coal", "Smooth Pebble" },
                new[] { "Coal", "Rough Geode", "Chunk of Silver Ore" },
                new[] { "Chunk of Silver Ore", "Rough Ruby", "Rough Emerald", "Moonstone Pebble" },
                new[] { "Chunk of Gold Ore", "Rough Sapphire", "Bloodstone Chip", "Glimmering Geode Fragment" },
                new[] { "Chunk of Platinum Ore", "Flawless Diamond", "Abyssal Opal", "Starshard Crystal" },
                new[] { "Prismatic Jewel", "Flawless Diamond", "Starshard Crystal", "Abyssal Opal" },
            };
            var pool = pools[Mathf.Clamp(tier - 1, 0, pools.Length - 1)];
            return pool[UnityEngine.Random.Range(0, pool.Length)];
        }

        private static string RollRefinedMetal(int tier)
        {
            if (tier <= 2) return RollCommonOre(tier);
            string[][] pools = {
                new[] { "Void Iron Chunk" },
                new[] { "Void Iron Chunk", "Soulsteel Billet" },
                new[] { "Soulsteel Billet", "Moonsilver Bar", "Sunforged Ingot" },
                new[] { "Starmetal Fragment", "Moonsilver Bar", "Sunforged Ingot", "Soulsteel Billet" },
            };
            var pool = pools[Mathf.Clamp(tier - 3, 0, pools.Length - 1)];
            return pool[UnityEngine.Random.Range(0, pool.Length)];
        }

        private static string RollEndgame(int tier)
        {
            if (tier <= 4) return RollRareOreOrGem(tier);
            if (tier == 5) { string[] p = { "Worldtree Splinter", "Petrified Heartwood" }; return p[UnityEngine.Random.Range(0, p.Length)]; }
            string[] p6 = { "Crystallized Time", "Essence of Eternity", "Worldtree Splinter", "Starmetal Fragment" };
            return p6[UnityEngine.Random.Range(0, p6.Length)];
        }
    }

    // ═══════════════════════════════════════════════════════════════════
    // ENDGAME MATERIAL DROPS FROM POWERFUL MOBS
    // ═══════════════════════════════════════════════════════════════════

    /// <summary>
    /// Drop endgame crafting materials (Thread of Fate, Essence of Eternity,
    /// Crystallized Time) from boss/named mobs in high-level zones.
    /// Hooks LootTable.LoadLootTable alongside existing recipe/meat drops.
    /// </summary>
    [HarmonyPatch(typeof(LootTable), "LoadLootTable")]
    public static class Patch_EndgameMaterialDrop
    {
        public static void Prefix(LootTable __instance)
        {
            if (!SkillsSaveManager.HasLoaded) return;
            if (!LootInjectionGuard.TryMark(__instance, LootInjectionKind.EndgameMaterial)) return;
            try
            {
                var character = __instance.GetComponent<Character>();
                if (character == null) return;
                int mobLevel = 0;
                bool isBoss = false;
                try
                {
                    mobLevel = character.MyStats?.Level ?? 0;
                    isBoss = character.BossXp > 1f;
                }
                catch { }

                // Only high-level mobs (25+) and bosses drop endgame materials
                if (mobLevel < 25) return;

                // Boss: 20% chance, Named/high: 5% chance
                float chance = isBoss ? 0.20f : 0.05f;
                if (UnityEngine.Random.value > chance) return;

                string[] endgameDrops;
                if (mobLevel >= 30 && isBoss)
                    endgameDrops = new[] { "Thread of Fate", "Essence of Eternity", "Crystallized Time" };
                else if (mobLevel >= 30)
                    endgameDrops = new[] { "Crystallized Time", "Worldtree Splinter", "Essence of Eternity" };
                else
                    endgameDrops = new[] { "Worldtree Splinter", "Petrified Heartwood" };

                string drop = endgameDrops[UnityEngine.Random.Range(0, endgameDrops.Length)];
                var item = Items.ItemFactory.GetItem(drop);
                if (item == null) return;

                if (__instance.ActualDrops.Count < 8)
                    __instance.ActualDrops.Add(item);
            }
            catch { }
        }
    }

    // ═══════════════════════════════════════════════════════════════════
    // BAG SPLIT PROTECTION — Prevent stack split from corrupting bag IDs
    // ═══════════════════════════════════════════════════════════════════

    /// <summary>
    /// Prevent the game's stack-split (Ctrl+click, Shift+click) from modifying
    /// bag item Quantity, which stores our permanent bag instance ID.
    /// </summary>
    [HarmonyPatch(typeof(ItemIcon), "InteractItemSlot")]
    public static class Patch_PreventBagSplit
    {
        internal struct ExternalMoveState
        {
            public Item Item;
            public int Identity;
            public ItemIcon Target;
            public Items.BagSystem.MutationSnapshot Mutation;
            public bool BankMove;
            public bool BankWithdrawal;
            public string BankPath;
            public string BankBefore;
            public string BankBagsPath;
            public string BankBagsBefore;
            public bool RestoreNoTrade;
            public bool PreviousNoTrade;
        }

        private static bool Prefix(ItemIcon __instance, ItemIcon ___SwapWith,
            ItemIcon ___MouseHomeSlot,
            out ExternalMoveState __state)
        {
            __state = new ExternalMoveState();
            try
            {
                if (__instance?.MyItem == null) return true;
                if (!Items.BagSystem.IsBag(__instance.MyItem.ItemName)) return true;

                // If a split key is held, block the interaction entirely
                bool splitOne = Input.GetKey(InputManager.SplitOne);
                bool splitTen = Input.GetKey(InputManager.SplitTen);
                if (__instance.BankSlot)
                {
                    if (splitOne || splitTen)
                    {
                        ChatHelper.Send(
                            "<color=#EF5350>[Bag]</color> Bags cannot be split " +
                            "or quick-withdrawn. Drag the bag into an empty " +
                            "inventory slot.");
                        return false;
                    }
                    // Normal pickup is captured when the cursor is dropped into
                    // a direct inventory slot. Returning it to a bank slot does
                    // not change its shared identity.
                    return true;
                }

                bool bankOrigin = ___MouseHomeSlot != null &&
                    ___MouseHomeSlot.BankSlot;
                if (bankOrigin)
                {
                    if (___SwapWith != null && ___SwapWith.BankSlot)
                        return true;
                    if (___SwapWith != null &&
                        Items.BagSystem.IsDirectInventorySlot(___SwapWith))
                    {
                        if (___SwapWith.MyItem != null &&
                            ___SwapWith.MyItem != GameData.PlayerInv.Empty)
                        {
                            ChatHelper.Send(
                                "<color=#EF5350>[Bag]</color> Withdraw a banked " +
                                "bag into an empty inventory slot.");
                            return false;
                        }
                        return CaptureBankWithdrawal(__instance, ___SwapWith,
                            ___MouseHomeSlot, ref __state);
                    }
                    Items.BagTransferSafety.SendBankRouteBlocked();
                    return false;
                }

                if (splitOne || splitTen)
                {
                    if (splitTen && !splitOne && GameData.VendorWindowOpen)
                    {
                        Items.BagTransferSafety.TryHandleVendorBagSale(
                            GameData.VendorWindow, __instance);
                        return false;
                    }
                    ChatHelper.Send(
                        "<color=#EF5350>[Bag]</color> Bags cannot be split or " +
                        "quick-banked. Drag the bag into an empty bank slot.");
                    return false;
                }

                if (___SwapWith != null && ___SwapWith.BankSlot)
                {
                    if (___SwapWith.MyItem != null &&
                        ___SwapWith.MyItem != GameData.PlayerInv.Empty)
                    {
                        ChatHelper.Send(
                            "<color=#EF5350>[Bag]</color> Put the bag in an " +
                            "empty bank slot.");
                        return false;
                    }
                    if (!Items.BagTransferSafety.CanBank(__instance))
                        return false;
                    return CaptureExternalMove(__instance, ___SwapWith, true,
                        ref __state);
                }
                if (___SwapWith != null && ___SwapWith.TrashSlot)
                {
                    if (!Items.BagTransferSafety.CanExternalize(__instance))
                        return false;
                    if (!CaptureExternalMove(__instance, ___SwapWith, false,
                        ref __state)) return false;
                    __state.RestoreNoTrade = true;
                    __state.PreviousNoTrade =
                        __instance.MyItem.NoTradeNoDestroy;
                    __instance.MyItem.NoTradeNoDestroy = false;
                    return true;
                }
                if (___SwapWith != null &&
                    (___SwapWith.LootSlot || ___SwapWith.VendorSlot ||
                     ___SwapWith.NotInInventory))
                {
                    Items.BagTransferSafety.SendTradeBlocked();
                    return false;
                }
            }
            catch (Exception ex)
            {
                SkillsPlugin.Log.LogWarning($"Bag move protection failed closed: {ex.Message}");
                return false;
            }
            return true;
        }

        private static void Postfix(ExternalMoveState __state)
        {
            if (__state.RestoreNoTrade && __state.Item != null)
                __state.Item.NoTradeNoDestroy =
                    __state.PreviousNoTrade;
            if (__state.Item == null || __state.Mutation == null) return;
            try
            {
                ItemIcon target = __state.Target;
                if (__state.BankMove && target == null &&
                    __state.BankWithdrawal)
                {
                    if (GameData.PlayerInv?.StoredSlots != null)
                        foreach (ItemIcon candidate in
                            GameData.PlayerInv.StoredSlots)
                            if (Items.BagSystem.IsDirectInventorySlot(candidate) &&
                                candidate.MyItem == __state.Item &&
                                candidate.Quantity == __state.Identity)
                            {
                                target = candidate;
                                break;
                            }
                }
                else if (__state.BankMove && target == null &&
                    GameData.BankUI?.Bank?.BankSlots != null)
                {
                    foreach (ItemIcon candidate in
                        GameData.BankUI.Bank.BankSlots)
                        if (candidate?.MyItem == __state.Item &&
                            candidate.Quantity == __state.Identity)
                        {
                            target = candidate;
                            break;
                        }
                }
                if (__state.BankWithdrawal)
                    Items.BagTransferSafety.FinalizeBankWithdrawal(
                        __state.Item, __state.Identity, target,
                        __state.Mutation, __state.BankPath,
                        __state.BankBefore, __state.BankBagsPath,
                        __state.BankBagsBefore);
                else if (__state.BankMove)
                    Items.BagTransferSafety.FinalizeBankDeposit(
                        __state.Item, __state.Identity, target,
                        __state.Mutation, __state.BankPath,
                        __state.BankBefore, __state.BankBagsPath,
                        __state.BankBagsBefore);
                else
                    Items.BagTransferSafety.FinalizeExternalMove(
                        __state.Item, __state.Identity, target,
                        __state.Mutation, false, null, null);
            }
            catch (Exception ex)
            {
                SkillsPlugin.Log.LogError(
                    $"Bag external move finalization failed: {ex.Message}");
                Items.BagSystem.RollbackExternalMutation(
                    __state.Mutation,
                    __state.BankMove ? __state.BankPath : null,
                    __state.BankMove ? __state.BankBefore : null,
                    __state.BankMove ? __state.BankBagsPath : null,
                    __state.BankMove ? __state.BankBagsBefore : null);
                Items.BagSystem.ClearCursorAfterExternalRollback();
                ReloadBankAfterRollback(__state.BankMove);
            }
        }

        private static Exception Finalizer(Exception __exception,
            ExternalMoveState __state)
        {
            if (__state.RestoreNoTrade && __state.Item != null)
                __state.Item.NoTradeNoDestroy =
                    __state.PreviousNoTrade;
            if (__exception != null && __state.Mutation != null)
            {
                Items.BagSystem.RollbackExternalMutation(
                    __state.Mutation,
                    __state.BankMove ? __state.BankPath : null,
                    __state.BankMove ? __state.BankBefore : null,
                    __state.BankMove ? __state.BankBagsPath : null,
                    __state.BankMove ? __state.BankBagsBefore : null);
                Items.BagSystem.ClearCursorAfterExternalRollback();
                ReloadBankAfterRollback(__state.BankMove);
            }
            return __exception;
        }

        private static bool CaptureExternalMove(ItemIcon source,
            ItemIcon target, bool bankMove, ref ExternalMoveState state)
        {
            state.Item = source.MyItem;
            state.Identity = Mathf.Max(1, source.Quantity);
            state.Target = target;
            state.Mutation =
                Items.BagSystem.BeginExternalBagMove(source);
            state.BankMove = bankMove;
            if (!bankMove) return true;
            state.BankPath = Items.BagTransferSafety.GetBankPath();
            state.BankBefore = Items.BagTransferSafety.BuildBankJson(
                GameData.BankUI?.Bank);
            if (!Items.BagTransferSafety.TryCaptureSharedBankState(
                out state.BankBagsPath, out state.BankBagsBefore))
            {
                state = new ExternalMoveState();
                return false;
            }
            return true;
        }

        private static bool CaptureBankWithdrawal(ItemIcon source,
            ItemIcon target, ItemIcon bankHome,
            ref ExternalMoveState state)
        {
            if (!Items.BagTransferSafety.TryCaptureSharedBankState(
                out string bankBagsPath, out string bankBagsBefore))
                return false;
            Item item = source.MyItem;
            int identity = Mathf.Max(1, source.Quantity);
            if (!Items.BagTransferSafety.CanWithdrawBankBag(
                item, identity, bankBagsBefore, out string reason))
            {
                Items.BagTransferSafety.SendBlocked(reason);
                return false;
            }
            state.Item = item;
            state.Identity = identity;
            state.Target = target;
            state.Mutation = Items.BagSystem.BeginExternalMutation();
            state.BankMove = true;
            state.BankWithdrawal = true;
            state.BankPath = Items.BagTransferSafety.GetBankPath();
            state.BankBefore = Items.BagTransferSafety.BuildBankJson(
                GameData.BankUI?.Bank, bankHome, item, identity);
            state.BankBagsPath = bankBagsPath;
            state.BankBagsBefore = bankBagsBefore;
            return true;
        }

        private static void ReloadBankAfterRollback(bool bankMove)
        {
            if (!bankMove || GameData.BankUI?.Bank == null) return;
            try { GameData.BankUI.Bank.LoadBank(); }
            catch (Exception ex)
            {
                SkillsPlugin.Log.LogError(
                    $"Bag bank UI rollback failed: {ex.Message}");
            }
        }
    }

    // ═══════════════════════════════════════════════════════════════════
    // BAG ITEM STORAGE — Right-click items to store in open bag
    // ═══════════════════════════════════════════════════════════════════

    /// <summary>
    /// Intercept right-clicks on inventory items when a bag window is open.
    /// If a bag is open and the player right-clicks a non-bag item,
    /// store it in the bag instead of the normal action.
    /// </summary>
    [HarmonyPatch(typeof(ItemIcon), "OnPointerUp")]
    public static class Patch_BagStoreOnRightClick
    {
        public static bool Prefix(ItemIcon __instance, UnityEngine.EventSystems.PointerEventData eventData)
        {
            try
            {
                // Native OnPointerUp immediately dereferences MyItem and its
                // Classes collection. Equipment removal and interrupted cursor
                // moves can briefly leave either field null, turning every
                // subsequent inventory click into a NullReferenceException.
                // Normalize those transient empty-slot states before any
                // add-on or native interaction logic runs.
                Inventory inventory = GameData.PlayerInv;
                NormalizeItemSlot(__instance, inventory);
                NormalizeItemSlot(GameData.MouseSlot, inventory);
                NormalizeItemSlot(GameData.ItemOnCursor, inventory);

                // True press-drag from a direct inventory slot into any crafted
                // bag cell. Handle this before Erenshor converts the source into
                // its click-to-cursor state.
                if (eventData.button ==
                        UnityEngine.EventSystems.PointerEventData.InputButton.Left &&
                    Items.BagWindow.AnyOpen &&
                    Items.BagSystem.IsDirectInventorySlot(__instance) &&
                    __instance.MyItem != null &&
                    __instance.MyItem != GameData.PlayerInv.Empty &&
                    !Items.BagSystem.IsBag(__instance.MyItem) &&
                    Items.BagWindow.TryHandleInventoryDragDrop(
                        __instance, eventData.position))
                    return false;

                // Only intercept right-clicks
                if (eventData.button != UnityEngine.EventSystems.PointerEventData.InputButton.Right)
                    return true;
                // Only when bag window is open
                if (!Items.BagWindow.AnyOpen) return true;
                // Let Erenshor finish an existing drag instead of mutating a
                // second slot and leaving the mouse cursor in a stuck state.
                if (GameData.ItemOnCursor != null ||
                    (GameData.MouseSlot != null && GameData.MouseSlot.dragging))
                    return true;
                // Only direct character inventory participates in the bag
                // transaction. In particular, never clear a global-bank slot.
                if (__instance.VendorSlot || __instance.LootSlot ||
                    __instance.BankSlot || __instance.TrashSlot ||
                    __instance.MouseSlot || __instance.NotInInventory) return true;
                // Skip empty slots
                if (__instance.MyItem == null || __instance.MyItem == GameData.PlayerInv.Empty)
                    return true;
                // Skip bag items (those open the bag instead)
                if (Items.BagSystem.IsBag(__instance.MyItem.ItemName)) return true;
                // Skip equipment slots
                if (GameData.PlayerInv.EquipmentSlots != null &&
                    GameData.PlayerInv.EquipmentSlots.Contains(__instance))
                    return true;

                // Store the item in the open bag
                if (Items.BagSystem.TryStoreFromInventorySlot(__instance))
                    return false; // Block normal right-click behavior
            }
            catch { }
            return true;
        }

        private static void NormalizeItemSlot(ItemIcon slot,
            Inventory inventory)
        {
            if (slot == null || inventory?.Empty == null) return;
            if (slot.MyItem == null)
            {
                slot.MyItem = inventory.Empty;
                slot.Quantity = 1;
                try { slot.UpdateSlotImage(); } catch { }
            }
            if (slot.MyItem?.Classes == null)
                slot.MyItem.Classes = new List<Class>();
        }
    }

    // ═══════════════════════════════════════════════════════════════════
    // FOOD TOLERANCE — XP on consume + duration bonus
    // ═══════════════════════════════════════════════════════════════════

    /// <summary>
    /// Award Food Tolerance XP whenever any consumable is used.
    /// Hooks the game's UseConsumable on ItemIcon.
    /// </summary>
    [HarmonyPatch(typeof(ItemIcon), "UseConsumable")]
    public static class Patch_FoodToleranceXP
    {
        public struct ConsumptionState
        {
            public Item Item;
            public string Name;
            public int Quantity;
        }

        [HarmonyPriority(Priority.First)]
        public static void Prefix(ItemIcon __instance, out ConsumptionState __state)
        {
            __state = new ConsumptionState();
            try
            {
                var item = __instance?.MyItem;
                if (item == null || item == GameData.PlayerInv?.Empty ||
                    item.ItemEffectOnClick == null || Items.BagSystem.IsBag(item) ||
                    Stations.RecipeVendors.IsRecipeScroll(item)) return;
                __state.Item = item;
                __state.Name = item.ItemName;
                __state.Quantity = Mathf.Max(1, __instance.Quantity);
            }
            catch (Exception ex)
            {
                SkillsPlugin.Log.LogWarning($"Could not capture consumable state: {ex.Message}");
            }
        }

        public static void Postfix(ItemIcon __instance, ConsumptionState __state)
        {
            try
            {
                if (!SkillsSaveManager.HasLoaded ||
                    !SkillsPlugin.CfgEnableFoodTolerance.Value || __state.Item == null) return;

                // Award XP only if the original invocation actually consumed one.
                // Reading MyItem only in Postfix loses the last item in a stack and
                // used to award XP even when casting/range checks rejected the use.
                bool consumed = !ReferenceEquals(__instance?.MyItem, __state.Item) ||
                    (__instance != null && __instance.Quantity < __state.Quantity);
                if (consumed)
                    Skills.FoodToleranceSkill.OnConsumeItem(__state.Name);
            }
            catch (Exception ex)
            {
                SkillsPlugin.Log.LogWarning($"Food Tolerance XP check failed: {ex.Message}");
            }
        }
    }

    /// <summary>
    /// Adjust only the exact status-effect slot successfully returned by the
    /// game's four-argument path. This avoids mutating a shared Spell asset and
    /// is safe under exceptions, nested effects, and re-entrant casts.
    /// </summary>
    [HarmonyPatch(typeof(Stats), "AddStatusEffect",
        new System.Type[] { typeof(Spell), typeof(bool), typeof(int), typeof(Character) })]
    public static class Patch_FoodToleranceDuration4
    {
        public static void Prefix(Spell spell, bool _fromPlayer,
            Character _specificCaster, ref int _dmgBonus)
        {
            try
            {
                if (!_fromPlayer || spell == null || spell.TargetDamage <= 0 ||
                    _specificCaster != GameData.PlayerControl?.Myself ||
                    Skills.MagicSkills.ClassifySpell(spell) !=
                        Skills.MagicSkills.MagicSchool.Conjuration) return;

                float multiplier = Skills.MagicSkills.GetConjurationMultiplier();
                if (multiplier <= 1f) return;

                int totalDamage = Mathf.Max(0, spell.TargetDamage + _dmgBonus);
                int scaledDamage = Mathf.RoundToInt(totalDamage * multiplier);
                _dmgBonus = Mathf.Max(0, scaledDamage - spell.TargetDamage);
            }
            catch { }
        }

        public static void Postfix(Stats __instance, Spell spell, bool _fromPlayer,
            Character _specificCaster, int __result)
        {
            try
            {
                if (!SkillsSaveManager.HasLoaded || spell == null || __result < 0 ||
                    __instance?.StatusEffects == null ||
                    __result >= __instance.StatusEffects.Length) return;
                var applied = __instance.StatusEffects[__result];
                if (applied == null || applied.Effect != spell) return;

                float multiplier = 1f;
                bool consumable = Items.ItemFactory.IsConsumableSpell(spell);
                if (consumable)
                {
                    if (!SkillsPlugin.CfgEnableFoodTolerance.Value ||
                        __instance.Myself == null || __instance.Myself.isNPC) return;
                    multiplier = 1f + Skills.FoodToleranceSkill.DurationBonus / 100f;
                }
                else if (_fromPlayer && _specificCaster == GameData.PlayerControl?.Myself &&
                    Skills.MagicSkills.ClassifySpell(spell) ==
                        Skills.MagicSkills.MagicSchool.Abjuration)
                {
                    multiplier = Skills.MagicSkills.GetBuffDurationMultiplier();
                }

                if (multiplier > 1f)
                    applied.Duration = Mathf.RoundToInt(applied.Duration * multiplier);
            }
            catch (Exception ex)
            {
                SkillsPlugin.Log.LogWarning($"Status duration adjustment failed: {ex.Message}");
            }
        }
    }

    // ═══════════════════════════════════════════════════════════════════
    // ═══════════════════════════════════════════════════════════════════
    // TOOLTIP INJECTION — Add category + weapon skill type to item tooltips
    // ═══════════════════════════════════════════════════════════════════

    /// <summary>
    /// Postfix on ItemInfoWindow.DisplayItem to inject extra info:
    /// - Food/Drink category for our consumables
    /// - Weapon skill type (1H Slashing, Piercing, etc.) for ALL weapons
    /// </summary>
    [HarmonyPatch(typeof(ItemInfoWindow), "DisplayItem")]
    public static class Patch_TooltipInject
    {
        public static void Postfix(ItemInfoWindow __instance, Item item, int _quantity)
        {
            try
            {
                if (item == null || __instance?.Lore == null) return;
                Items.NativeConsumableCompatibility
                    .AppendCustomClassesToTooltip(__instance, item);
                string lore = __instance.Lore.text ?? "";

                // Recipe scroll — show Learned/Unlearned status
                if (Stations.RecipeVendors.IsRecipeScroll(item))
                {
                    string recipeName = item.ItemName.Substring(8); // Remove "Recipe: "
                    if (SkillsSaveManager.HasLoaded &&
                        SkillsSaveManager.Data.IsRecipeKnown(recipeName))
                        lore = AppendVisibleLineOnce(lore, "[Already Learned]",
                            "<color=#888888>[Already Learned]</color>");
                    else
                        lore = AppendVisibleLineOnce(lore, "[Right-click to Learn]",
                            "<color=#00FF00>[Right-click to Learn]</color>");
                }

                // 1. Food/Drink category — already in Lore at registration, skip duplicate

                // 2. Weapon skill type for ANY weapon (game or custom).
                // Use the item's own flags; currently equipped gear must not
                // change a hovered item's classification.
                bool isWeapon = item.WeaponDmg > 0 || item.WeaponDly > 0 ||
                    item.IsBow || item.IsWand ||
                    item.ThisWeaponType != Item.WeaponType.None;
                if (isWeapon)
                {
                    string lower = (item.ItemName ?? "").ToLowerInvariant();
                    bool isTwoHand =
                        item.ThisWeaponType == Item.WeaponType.TwoHandMelee ||
                        item.ThisWeaponType == Item.WeaponType.TwoHandStaff ||
                        item.ThisWeaponType == Item.WeaponType.TwoHandBow ||
                        lower.Contains("great") || lower.Contains("staff") ||
                        lower.Contains("halberd") || lower.Contains("claymore") ||
                        lower.Contains("maul") || lower.Contains("pole") ||
                        lower.Contains("totem") || lower.Contains("warbow") ||
                        lower.Contains("longbow");

                    var wtype = Skills.CombatSkills.ClassifyWeapon(
                        item, isTwoHand);
                    if (wtype != Skills.CombatSkills.WeaponType.Unknown)
                    {
                        string typeName = Skills.CombatSkills.GetTypeName(wtype);
                        string visible = $"Skill: {typeName}";
                        AppendWeaponSkillInfo(__instance, visible);
                    }
                }

                __instance.Lore.text = lore;
                Items.NativeConsumableCompatibility
                    .FitTooltipText(__instance.Lore);
            }
            catch { }
        }

        /// <summary>
        /// Weapon proficiency belongs beside Erenshor's native Base DPS line,
        /// not inside item lore. Keeping generated stats out of Lore prevents
        /// them from colliding with authored flavor text on compact tooltips.
        /// </summary>
        private static void AppendWeaponSkillInfo(ItemInfoWindow window,
            string visible)
        {
            if (window?.OtherTextParent == null ||
                string.IsNullOrWhiteSpace(visible))
                return;
            var text = window.OtherTextParent
                .GetComponent<TMPro.TextMeshProUGUI>();
            if (text == null)
                text = window.OtherTextParent
                    .GetComponentInChildren<TMPro.TextMeshProUGUI>(true);
            if (text == null) return;
            window.OtherTextParent.SetActive(true);
            // Native lays this label out for exactly one line ("Base DPS: N")
            // and anchors it so extra lines grow back over the lore above.
            // Shrinking the text does not help because it is the block height
            // that collides, so extend the existing line sideways instead of
            // starting a new one.
            string current = text.text ?? "";
            // Substring rather than whole-line: the proficiency now shares a
            // line with Base DPS, so a line-equality test would never match and
            // it would be re-appended on every DisplayItem call.
            if (!ContainsVisibleText(current, visible))
            {
                string rich = $"<color=#FF9800>{visible}</color>";
                text.text = string.IsNullOrWhiteSpace(current)
                    ? rich
                    : current.TrimEnd('\r', '\n') + "   " + rich;
            }
            Items.NativeConsumableCompatibility.FitTooltipText(text);
        }

        /// <summary>
        /// Adds a generated tooltip line only when the same visible line is not
        /// already present. Rich-text colors may be rebuilt or stripped by the
        /// game between DisplayItem calls, so compare rendered text, not markup.
        /// </summary>
        public static string AppendVisibleLineOnce(string current,
            string visibleLine, string richLine)
        {
            string text = current ?? "";
            if (string.IsNullOrWhiteSpace(visibleLine) ||
                string.IsNullOrWhiteSpace(richLine) ||
                ContainsVisibleLine(text, visibleLine))
                return text;
            return text + "\n" + richLine;
        }

        /// <summary>
        /// True when the rendered text already contains this fragment anywhere,
        /// ignoring rich-text markup. Used for generated text that shares a
        /// line with native content.
        /// </summary>
        public static bool ContainsVisibleText(string text, string fragment)
        {
            if (string.IsNullOrEmpty(text) || string.IsNullOrWhiteSpace(fragment))
                return false;
            return StripRichText(text).IndexOf(fragment.Trim(),
                StringComparison.OrdinalIgnoreCase) >= 0;
        }

        public static bool ContainsVisibleLine(string text, string visibleLine)
        {
            if (string.IsNullOrEmpty(text) || string.IsNullOrEmpty(visibleLine))
                return false;
            string[] lines = text.Replace("\r\n", "\n").Replace('\r', '\n')
                .Split('\n');
            for (int i = 0; i < lines.Length; i++)
            {
                if (string.Equals(StripRichText(lines[i]).Trim(),
                    visibleLine.Trim(), StringComparison.OrdinalIgnoreCase))
                    return true;
            }
            return false;
        }

        public static string StripRichText(string text)
        {
            if (string.IsNullOrEmpty(text)) return text ?? "";
            var visible = new System.Text.StringBuilder(text.Length);
            bool inTag = false;
            for (int i = 0; i < text.Length; i++)
            {
                char ch = text[i];
                if (ch == '<') { inTag = true; continue; }
                if (ch == '>' && inTag) { inTag = false; continue; }
                if (!inTag) visible.Append(ch);
            }
            return visible.ToString();
        }
    }

    // ═══════════════════════════════════════════════════════════════════
    // SPELL TOOLTIP — Show magic school on spell details
    // ═══════════════════════════════════════════════════════════════════

    /// <summary>
    /// Inject magic school into the spell details window (right-click item procs,
    /// hotbar spell info). Hooks ItemInfoWindow.LoadSpellDetails.
    /// </summary>
    [HarmonyPatch(typeof(ItemInfoWindow), "LoadSpellDetails")]
    public static class Patch_SpellTooltip
    {
        public static void Postfix(ItemInfoWindow __instance, Spell AssignedSpell)
        {
            if (!SkillsPlugin.CfgEnableMagicSkills.Value) return;
            if (!ShouldAnnotate(AssignedSpell)) return;
            try
            {
                var school = Skills.MagicSkills.ClassifySpell(AssignedSpell);
                if (__instance.SpellDetailsDesc != null)
                    __instance.SpellDetailsDesc.text = AppendSchoolTagOnce(
                        __instance.SpellDetailsDesc.text, school);
            }
            catch { }
        }

        public static bool ShouldAnnotate(Spell spell)
        {
            return spell != null && !Items.ItemFactory.IsConsumableSpell(spell) &&
                Skills.MagicSkills.ClassifySpell(spell) !=
                    Skills.MagicSkills.MagicSchool.None;
        }

        public static string AppendSchoolTagOnce(string current,
            Skills.MagicSkills.MagicSchool school)
        {
            string text = current ?? "";
            string tag = GetSchoolTag(school);
            string visible = Patch_TooltipInject.StripRichText(tag).Trim();
            return Patch_TooltipInject.AppendVisibleLineOnce(
                text, visible, tag);
        }

        public static string GetSchoolTag(Skills.MagicSkills.MagicSchool school)
        {
            switch (school)
            {
                case Skills.MagicSkills.MagicSchool.Evocation:
                    return "<color=#FF6B35>School: Evocation</color>";
                case Skills.MagicSkills.MagicSchool.Abjuration:
                    return "<color=#4FC3F7>School: Abjuration</color>";
                case Skills.MagicSkills.MagicSchool.Alteration:
                    return "<color=#66BB6A>School: Alteration</color>";
                case Skills.MagicSkills.MagicSchool.Conjuration:
                    return "<color=#AB47BC>School: Conjuration</color>";
                default:
                    return "";
            }
        }
    }

    /// <summary>
    /// Inject magic school into the spell book tooltip.
    /// When a spell is selected in the spell book, ToggleSelect builds
    /// the description text and sets it on the Desc TextMeshProUGUI field.
    /// We postfix to append the school name.
    /// </summary>
    [HarmonyPatch(typeof(SpellbookSlot), "ToggleSelect")]
    public static class Patch_SpellBookTooltip
    {
        public static void Postfix(SpellbookSlot __instance, bool _selected)
        {
            if (!SkillsPlugin.CfgEnableMagicSkills.Value) return;
            if (!_selected) return;
            if (__instance.AssignedSpell == null) return;
            try
            {
                var school = Skills.MagicSkills.ClassifySpell(__instance.AssignedSpell);
                if (school == Skills.MagicSkills.MagicSchool.None) return;
                if (__instance.Desc != null)
                    __instance.Desc.text = Patch_SpellTooltip.AppendSchoolTagOnce(
                        __instance.Desc.text, school);
            }
            catch { }
        }
    }

    /// <summary>
    /// Inject magic school into the hotbar hover tooltip.
    /// When the player hovers over a spell on the hotbar, Hotkeys.OnPointerEnter
    /// populates GameData.Misc.SpellDesc. We postfix to append school info.
    /// </summary>
    [HarmonyPatch(typeof(Hotkeys), "OnPointerEnter")]
    public static class Patch_HotbarSpellTooltip
    {
        public static void Postfix(Hotkeys __instance)
        {
            if (!SkillsPlugin.CfgEnableMagicSkills.Value) return;
            try
            {
                // Only for spell-type hotkeys
                if (__instance.thisHK != Hotkeys.HKType.Spell) return;
                if (__instance.AssignedSpell == null) return;

                var school = Skills.MagicSkills.ClassifySpell(__instance.AssignedSpell);
                if (school == Skills.MagicSkills.MagicSchool.None) return;
                if (GameData.Misc?.SpellDesc != null)
                    GameData.Misc.SpellDesc.text = Patch_SpellTooltip.AppendSchoolTagOnce(
                        GameData.Misc.SpellDesc.text, school);
            }
            catch { }
        }
    }

    // CHAT COMMANDS
    // ═══════════════════════════════════════════════════════════════════

    /// <summary>
    /// Add chat commands for all skills:
    /// /skills - Overview of all skill levels
    /// /extrasettings or /logcolors - Open the standalone addon settings book
    /// /fishskill, /foraging, /swimming, etc. - Individual skill info
    /// /forage - Attempt to forage (alias for keybind)
    /// /bindwound - Attempt to bind wounds
    /// /beg - Attempt to beg
    /// /meditate - Toggle meditation
    /// /senseheading - Sense heading
    /// </summary>
    [HarmonyPatch(typeof(TypeText), "CheckCommands")]
    public static class Patch_ChatCommands
    {
        public static bool Prefix(TypeText __instance)
        {
            try
            {
                // typed is a UI input field component, not a string
                // Access it the same way ErenshorQoL does: __instance.typed.text
                string raw = __instance.typed.text;

                if (string.IsNullOrEmpty(raw)) return true;
                string cmd = raw.Trim().ToLowerInvariant();
                if (string.IsNullOrEmpty(cmd)) return true;

                // ── Opt-in exhaustive in-game test harness ───────────
                if (cmd == "/cstest" || cmd.StartsWith("/cstest ") ||
                    cmd == "/csitem" || cmd.StartsWith("/csitem "))
                {
                    Testing.AddonTestMode.HandleChatCommand(raw);
                    return false;
                }

                // ── Opt-in, crash-safe corpse runs ──────────────────
                if (cmd == "/corpserun" || cmd.StartsWith("/corpserun "))
                {
                    Features.CorpseRunSystem.HandleChatCommand(raw);
                    return false;
                }

                // ── Standalone Extra Settings book ──────────────────
                if (cmd == "/extrasettings" || cmd == "/logcolors")
                {
                    UI.LogDisplaySettingsUI.Open();
                    return false;
                }

                // ── /skills — Master overview ────────────────────────
                if (cmd == "/skills" || cmd == "/skilllist")
                {
                    ShowSkillOverview();
                    return false;
                }

                // ── /scanobjects — Debug: list game objects matching keywords ──
                if (cmd.StartsWith("/scanobjects"))
                {
                    if (!RequireDebugCommands()) return false;
                    string filter = cmd.Length > 13 ? cmd.Substring(13).Trim().ToLowerInvariant() : "";
                    string[] keywords = string.IsNullOrEmpty(filter)
                        ? new[] { "oven", "brew", "brewing", "barrel", "bench", "fire", "loom", "forge" }
                        : new[] { filter };
                    var allObjs = UnityEngine.Object.FindObjectsOfType<UnityEngine.GameObject>();
                    int count = 0;
                    ChatHelper.Send("<color=#00FF00>[Scan]</color> Searching game objects...");
                    foreach (var obj in allObjs)
                    {
                        if (obj == null) continue;
                        string lower = obj.name.ToLowerInvariant();
                        foreach (var kw in keywords)
                        {
                            if (lower.Contains(kw))
                            {
                                var pos = obj.transform.position;
                                bool hasCol = obj.GetComponent<UnityEngine.Collider>() != null;
                                ChatHelper.Send($"<color=#AAAAAA>[{(hasCol ? "COL" : "---")}]</color> " +
                                    $"<color=#FFFFFF>{obj.name}</color> " +
                                    $"<color=#888888>({pos.x:F1}, {pos.y:F1}, {pos.z:F1})</color>");
                                count++;
                                if (count >= 100) break;
                                break;
                            }
                        }
                        if (count >= 100) break;
                    }
                    ChatHelper.Send($"<color=#00FF00>[Scan]</color> Found {count}" +
                        (count >= 100 ? "+ objects (output capped)." : " objects."));
                    return false;
                }

                // ── /givebag — Debug: give a bag item ───────────────
                if (cmd.StartsWith("/givebag"))
                {
                    if (!RequireDebugCommands()) return false;
                    string bagName = cmd.Length > 9 ? cmd.Substring(9).Trim() : "";
                    if (string.IsNullOrEmpty(bagName))
                    {
                        ChatHelper.Send("<color=#00FF00>[Debug]</color> Bags: Silken Pouch, Herbalist's Satchel, Simple Backpack, Adventurer's Backpack");
                        ChatHelper.Send("<color=#00FF00>[Debug]</color> Usage: /givebag <name>");
                        return false;
                    }
                    // Match partial name
                    string[] bags = { "Silken Pouch", "Herbalist's Satchel", "Simple Backpack", "Adventurer's Backpack" };
                    string matched = null;
                    foreach (var b in bags)
                        if (b.IndexOf(bagName, StringComparison.OrdinalIgnoreCase) >= 0)
                        { matched = b; break; }
                    if (matched == null)
                    {
                        ChatHelper.Send($"<color=#FF6666>[Debug]</color> Unknown bag: {bagName}");
                        return false;
                    }
                    if (Items.ItemFactory.GiveItemToPlayer(matched))
                        ChatHelper.Send($"<color=#00FF00>[Debug]</color> Gave you: {matched}");
                    else
                        ChatHelper.Send($"<color=#FF6666>[Debug]</color> Failed to give: {matched}");
                    return false;
                }

                // ── /givetp — Debug: grant training points ────────────
                if (cmd.StartsWith("/givetp"))
                {
                    if (!RequireDebugCommands()) return false;
                    string numStr = cmd.Length > 8 ? cmd.Substring(8).Trim() : "10";
                    int amount = 10;
                    if (!int.TryParse(numStr, out amount)) amount = 10;
                    amount = Mathf.Clamp(amount, 1, 1000);
                    // Decrease spent so the recalculation gives more TP
                    int beforeSpent = SkillsSaveManager.Data.TrainingPointsSpent;
                    SkillsSaveManager.Data.TrainingPointsSpent =
                        Mathf.Max(0, SkillsSaveManager.Data.TrainingPointsSpent - amount);
                    SkillsSaveManager.Data.CheckForNewTrainingPoints();
                    int refunded = beforeSpent - SkillsSaveManager.Data.TrainingPointsSpent;
                    ChatHelper.Send(
                        $"<color=#00FF00>[Debug]</color> Refunded {refunded} spent training points. " +
                        $"Total: {SkillsSaveManager.Data.TrainingPoints}");
                    SkillsSaveManager.Save();
                    return false;
                }

                // ── /learnrecipe — Debug: learn a recipe by name ─────
                if (cmd.StartsWith("/learnrecipe"))
                {
                    if (!RequireDebugCommands()) return false;
                    string rname = cmd.Length > 13 ? cmd.Substring(13).Trim() : "";
                    if (string.IsNullOrEmpty(rname))
                    {
                        ChatHelper.Send("<color=#00FF00>[Debug]</color> Usage: /learnrecipe <recipe name>");
                        return false;
                    }
                    if (string.Equals(rname, "all", StringComparison.OrdinalIgnoreCase))
                    {
                        var data = SkillsSaveManager.Data;
                        var knownBefore = new List<string>(data.KnownRecipes);
                        int learned = 0;
                        string[] tsList = { "Smithing", "Baking", "Brewing", "Fletching", "Jewelcraft", "Tailoring" };
                        foreach (var ts in tsList)
                        {
                            var recipes = Skills.Tradeskills.GetRecipesForSkillPublic(ts);
                            foreach (var r in recipes)
                                if (data.LearnRecipe(r.Name)) learned++;
                        }
                        if (!SkillsSaveManager.SaveRecipesFile())
                        {
                            data.KnownRecipes.Clear();
                            data.KnownRecipes.AddRange(knownBefore);
                            ChatHelper.Send("<color=#EF5350>[Debug]</color> Recipe unlocks were rolled back because the save failed.");
                            return false;
                        }
                        ChatHelper.Send($"<color=#00FF00>[Debug]</color> Learned {learned} new recipes.");
                        return false;
                    }
                    // Find recipe by partial name
                    string[] allTS = { "Smithing", "Baking", "Brewing", "Fletching", "Jewelcraft", "Tailoring" };
                    foreach (var ts in allTS)
                    {
                        var recipes = Skills.Tradeskills.GetRecipesForSkillPublic(ts);
                        foreach (var r in recipes)
                        {
                            if (r.Name.IndexOf(rname,
                                StringComparison.OrdinalIgnoreCase) >= 0)
                            {
                                if (SkillsSaveManager.Data.LearnRecipe(r.Name))
                                {
                                    if (!SkillsSaveManager.SaveRecipesFile())
                                    {
                                        SkillsSaveManager.Data.KnownRecipes.Remove(r.Name);
                                        ChatHelper.Send("<color=#EF5350>[Debug]</color> The recipe was not learned because the save failed.");
                                        return false;
                                    }
                                    ChatHelper.Send($"<color=#FFD700>[Skills]</color> Learned recipe: <color=#FFFFFF>{r.Name}</color> ({ts})");
                                }
                                else
                                    ChatHelper.Send($"<color=#AAAAAA>[Skills]</color> You already know {r.Name}.");
                                return false;
                            }
                        }
                    }
                    ChatHelper.Send($"<color=#FF6666>[Skills]</color> No recipe found matching: {rname}");
                    return false;
                }

                // /buyrecipe removed — use recipe trainer NPCs instead
                if (cmd.StartsWith("/buyrecipe"))
                {
                    ChatHelper.Send("<color=#FF9800>[Skills]</color> Visit a Recipe Trainer NPC to buy recipes.");
                    return false;
                }
                // ── /recipes — Show known recipe count ────────────────
                if (cmd == "/recipes")
                {
                    var data = SkillsSaveManager.Data;
                    int total = 0;
                    string[] tsList = { "Smithing", "Baking", "Brewing", "Fletching", "Jewelcraft", "Tailoring" };
                    foreach (var ts in tsList)
                        total += Skills.Tradeskills.GetRecipesForSkillPublic(ts).Count;
                    ChatHelper.Send(
                        $"<color=#FFD700>[Skills]</color> Known recipes: " +
                        $"<color=#FFFFFF>{data.KnownRecipes.Count}</color> / {total}");
                    return false;
                }

                // ── /train <skillname> ──────────────────────────────
                if (cmd.StartsWith("/train"))
                {
                    string skillName = cmd.Length > 7 ? cmd.Substring(7).Trim() : "";
                    if (string.IsNullOrEmpty(skillName))
                    {
                        var data = SkillsSaveManager.Data;
                        ChatHelper.Send(
                            $"<color=#FFD700>[Skills]</color> " +
                            $"Training points: <color=#00FF00>{data.TrainingPoints}</color>. " +
                            $"Usage: /train <skill name>");
                    }
                    else
                    {
                        var data = SkillsSaveManager.Data;
                        // Use FindSkillByName to get the ACTUAL field reference
                        SkillEntry match = data.FindSkillByName(skillName);
                        if (match == null)
                            ChatHelper.Send($"<color=#FF6666>[Skills]</color> Unknown skill: {skillName}");
                        else if (data.TrainingPoints <= 0)
                            ChatHelper.Send("<color=#FF6666>[Skills]</color> No training points available.");
                        else if (match.IsAtCap)
                            ChatHelper.Send($"<color=#FF6666>[Skills]</color> {match.Name} is at its skill cap.");
                        else if (data.SpendTrainingPoint(match))
                        {
                            ChatHelper.Send(
                                $"<color=#FFD700>[Skills]</color> " +
                                $"Trained <color=#FFFFFF>{match.Name}</color> to level {match.Level}! " +
                                $"<color=#AAAAAA>({data.TrainingPoints} TP remaining)</color>");
                            SkillsSaveManager.Save();
                        }
                    }
                    return false;
                }

                // ── Individual skill info ────────────────────────────
                if (cmd == "/fishskill" || cmd == "/fishing")
                { ShowSkillDetail(SkillsSaveManager.Data.Fishing, "#7EC8E3"); return false; }
                if (cmd == "/foraging" || cmd == "/forageskill")
                { ShowSkillDetail(SkillsSaveManager.Data.Foraging, "#8BC34A"); return false; }
                if (cmd == "/swimming" || cmd == "/swimskill")
                { ShowSkillDetail(SkillsSaveManager.Data.Swimming, "#4FC3F7"); return false; }

                if (cmd == "/bindwoundskill")
                { ShowSkillDetail(SkillsSaveManager.Data.BindWound, "#EF5350"); return false; }
                if (cmd == "/meditateskill")
                { ShowSkillDetail(SkillsSaveManager.Data.Meditate, "#CE93D8"); return false; }

                if (cmd == "/toleranceskill" || cmd == "/foodtolerance")
                { ShowSkillDetail(SkillsSaveManager.Data.FoodTolerance, "#FFAB40"); return false; }
                if (cmd == "/beggingskill")
                { ShowSkillDetail(SkillsSaveManager.Data.Begging, "#BCAAA4"); return false; }

                // ── Combat skill commands ────────────────────────────
                if (cmd == "/weaponskills" || cmd == "/combat" || cmd == "/combatskills")
                {
                    ShowCombatSkillOverview();
                    return false;
                }

                // ── Action commands (aliases for keybinds) ───────────
                if (cmd == "/forage")
                { Skills.ForagingSkill.TryForage(); return false; }
                if (cmd == "/bindwound" || cmd == "/bandage")
                { Skills.BindWoundSkill.TryBindWound(); return false; }
                if (cmd == "/beg")
                { Skills.BeggingSkill.TryBeg(); return false; }
                if (cmd == "/meditate" || cmd == "/med")
                { Skills.MeditateSkill.ToggleMeditate(); return false; }


                // ── /dumpdb — Export game databases to CSV ────────
                if (cmd == "/dumpdb")
                {
                    if (!RequireDebugCommands()) return false;
                    DatabaseDumper.DumpAll(); return false;
                }

                // ── /iconcheck — List items missing game icons ───────
                if (cmd == "/iconcheck")
                {
                    if (!RequireDebugCommands()) return false;
                    Items.ItemFactory.ReportMissingIcons(); return false;
                }

                // ── Tradeskill commands — text shortcuts only ─────
                string[] tradeskillNames = { "Smithing", "Baking", "Brewing",
                    "Fletching", "Jewelcraft", "Tailoring" };

                foreach (var ts in tradeskillNames)
                {
                    string prefix = "/" + ts.ToLowerInvariant();
                    // /smithing list — show known recipes in chat
                    if (cmd == prefix + " list")
                    {
                        Skills.Tradeskills.ListRecipes(ts);
                        return false;
                    }
                }

                if (cmd == "/tradeskills" || cmd == "/trades")
                {
                    ShowTradeskillOverview();
                    return false;
                }

                // ── /whisper SimName craft ... — SimCraft whisper system ──
                if (cmd.StartsWith("/whisper "))
                {
                    // Parse: /whisper SimName message
                    // The game parses from char 9 to first space = name, rest = message
                    string remainder = raw.Substring(8).Trim(); // strip "/whisper "
                    int spaceIdx = remainder.IndexOf(' ');
                    if (spaceIdx > 0)
                    {
                        string targName = remainder.Substring(0, spaceIdx).Trim();
                        string whisperMsg = remainder.Substring(spaceIdx + 1).Trim();

                        if (!string.IsNullOrEmpty(targName) && !string.IsNullOrEmpty(whisperMsg))
                        {
                            // Find the sim
                            SimPlayerTracking simTracking = null;
                            if (GameData.SimMngr != null)
                            {
                                foreach (var sim in GameData.SimMngr.Sims)
                                {
                                    if (string.Equals(sim.SimName, targName,
                                        StringComparison.OrdinalIgnoreCase))
                                    {
                                        simTracking = sim;
                                        break;
                                    }
                                }
                            }

                            if (simTracking != null && !simTracking.IsGMCharacter)
                            {
                                if (Skills.SimPlayerTradeskills.TryHandleWhisper(simTracking, whisperMsg))
                                {
                                    // Log the outgoing whisper ourselves
                                    UpdateSocialLog.LogAdd(new ChatLogLine(
                                        "[WHISPER TO] " + simTracking.SimName + ": " + whisperMsg,
                                        ChatLogLine.LogType.Whisper, "#FF62D1"));
                                    return false; // handled, skip game's CheckCommands
                                }
                            }
                        }
                    }
                    // Fall through to let game handle it normally
                }
            }
            catch (Exception ex)
            {
                SkillsPlugin.Log.LogError($"Chat command error: {ex}");
            }
            return true;
        }

        private static bool RequireDebugCommands()
        {
            if (SkillsPlugin.CfgEnableDebugCommands?.Value == true) return true;
            ChatHelper.Send(
                "<color=#EF5350>[Classic Skills]</color> Debug commands are disabled. " +
                "Enable Testing.EnableDebugCommands in the config only for controlled testing.");
            return false;
        }

        /// <summary>Gold cost for a recipe based on its trivial level.</summary>
        private static int GetRecipeCost(int trivialLevel)
        {
            if (trivialLevel <= 20) return 10;
            if (trivialLevel <= 40) return 50;
            if (trivialLevel <= 60) return 150;
            if (trivialLevel <= 80) return 400;
            if (trivialLevel <= 100) return 800;
            if (trivialLevel <= 120) return 1500;
            if (trivialLevel <= 140) return 3000;
            if (trivialLevel <= 160) return 6000;
            return 10000; // 161-180 endgame
        }

        private static void ShowSkillOverview()
        {
            var data = SkillsSaveManager.Data;
            int max = SkillsPlugin.CfgGlobalMaxLevel.Value;

            ChatHelper.Send(
                $"<color=#FFD700>══════ Utility Skills ══════</color>");

            foreach (var s in data.GetUtilitySkills())
            {
                string bar = MiniBar(s.LevelProgress, 10);
                string lvl = s.IsMaxLevel
                    ? $"<color=#FFD700>{s.Level}</color>"
                    : $"{s.Level}";

                ChatHelper.Send(
                    $"  <color=#FFFFFF>{s.Name,-20}</color> " +
                    $"Lv {lvl}/{max} {bar} " +
                    $"<color=#AAAAAA>(Used {s.TimesUsed}x)</color>");
            }

            ChatHelper.Send(
                $"<color=#FF6E40>══════ Combat Skills ═══════</color>");

            foreach (var entry in Skills.CombatSkills.GetActiveCombatSkills())
            {
                var s = entry.Skill;
                string bar = MiniBar(s.LevelProgress, 10);
                string lvl = s.IsMaxLevel
                    ? $"<color=#FFD700>{s.Level}</color>"
                    : $"{s.Level}";
                string bonus = entry.Bonus > 0.01f
                    ? $" <color=#FF6E40>(+{entry.Bonus:F1}% dmg)</color>"
                    : "";

                ChatHelper.Send(
                    $"  <color=#FFFFFF>{entry.Name,-20}</color> " +
                    $"Lv {lvl}/{max} {bar}{bonus}");
            }

            ChatHelper.Send(
                $"<color=#FFD700>════════════════════════════</color>");
            ChatHelper.Send(
                $"<color=#AAAAAA>Press {SkillsPlugin.CfgSkillWindowKey.Value} " +
                $"for the full skill window. /weaponskills for combat details.</color>");
        }

        private static void ShowCombatSkillOverview()
        {
            int max = SkillsPlugin.CfgGlobalMaxLevel.Value;

            ChatHelper.Send(
                $"<color=#FF6E40>═══ Combat Weapon Skills ═══</color>");

            foreach (var entry in Skills.CombatSkills.GetActiveCombatSkills())
            {
                var s = entry.Skill;
                string bar = MiniBar(s.LevelProgress, 12);
                string bonus = entry.Bonus > 0.01f
                    ? $"<color=#FF6E40>+{entry.Bonus:F1}% damage</color>"
                    : "<color=#AAAAAA>no bonus yet</color>";

                ChatHelper.Send(
                    $"  <color=#FFFFFF>{entry.Name,-16}</color> " +
                    $"Lv {s.Level}/{max} {bar} {bonus} " +
                    $"<color=#666666>({s.TimesUsed} hits)</color>");
            }

            ChatHelper.Send(
                $"<color=#AAAAAA>Damage bonus: " +
                $"+{SkillsPlugin.CfgCombatSkillDamagePerLevel.Value:F1}% per level " +
                $"(max +{SkillsPlugin.CfgCombatSkillDamagePerLevel.Value * max:F0}% at level {max})</color>");
            ChatHelper.Send(
                $"<color=#FF6E40>════════════════════════════</color>");
        }

        private static void ShowTradeskillOverview()
        {
            int max = SkillsPlugin.CfgGlobalMaxLevel.Value;
            var data = SkillsSaveManager.Data;

            ChatHelper.Send(
                $"<color=#FF9800>═══ Tradeskills ═══</color>");

            foreach (var s in data.GetTradeskills())
            {
                string bar = MiniBar(s.LevelProgress, 10);
                string lvl = s.IsMaxLevel
                    ? $"<color=#FFD700>{s.Level}</color>"
                    : $"{s.Level}";

                ChatHelper.Send(
                    $"  <color=#FFFFFF>{s.Name,-16}</color> " +
                    $"Lv {lvl}/{max} {bar} " +
                    $"<color=#AAAAAA>({s.Successes} crafted | {s.Failures} failed)</color>");
            }

            ChatHelper.Send(
                $"<color=#AAAAAA>Use /<tradeskill> list to see recipes.</color>");
            ChatHelper.Send(
                $"<color=#FF9800>═══════════════════</color>");
        }

        private static void ShowSkillDetail(SkillEntry skill, string color)
        {
            int max = SkillsPlugin.CfgGlobalMaxLevel.Value;
            ChatHelper.Send(
                $"<color={color}>═══ {skill.Name} ═══</color>");
            ChatHelper.Send(
                $"<color={color}>Level:</color> {skill.Level}/{max}");
            if (!skill.IsMaxLevel)
                ChatHelper.Send(
                    $"<color={color}>XP:</color> " +
                    $"{skill.CurrentXp:F0}/{skill.XpToNextLevel:F0} " +
                    $"({skill.LevelProgress * 100f:F1}%)");
            ChatHelper.Send(
                $"<color={color}>Used:</color> {skill.TimesUsed} times " +
                $"| {skill.Successes} successes " +
                $"| {skill.Failures} failures");
            ChatHelper.Send(
                $"<color={color}>═══════════════════</color>");
        }

        private static string MiniBar(float progress, int width)
        {
            int filled = Mathf.RoundToInt(progress * width);
            string result = "[";
            for (int i = 0; i < width; i++)
                result += (i < filled) ? "█" : "░";
            result += "]";
            return result;
        }
    }

    /// <summary>
    /// Apply combat skill damage bonus to melee damage calculations.
    /// Multiplies the damage result by the player's weapon skill bonus.
    /// </summary>
    [HarmonyPatch(typeof(Stats), "CalcMeleeDamage")]
    public static class Patch_CombatDamageBonus_Melee
    {
        public static void Postfix(Stats __instance, ref int __result)
        {
            try
            {
                if (__result <= 0) return;

                // Sim player melee bonus
                if (__instance.Myself != null && Skills.SimPlayerSkills.IsSimPlayer(__instance.Myself))
                {
                    float simMult = Skills.SimPlayerSkills.GetMeleeDamageMultiplier(__instance.Myself);
                    if (simMult > 1f)
                        __result = Mathf.RoundToInt(__result * simMult);
                    return;
                }

                // Player melee bonus
                if (!SkillsPlugin.CfgEnableCombatSkills.Value) return;
                if (__instance.Myself == null) return;
                var pc = GameData.PlayerControl;
                if (pc == null || __instance.Myself != pc) return;

                bool mainHand = !Patches.Patch_MeleeXP.ContextActive ||
                    Patches.Patch_MeleeXP.CurrentIsMainHand;
                Item weapon = mainHand
                    ? __instance.MyInv?.MH?.MyItem
                    : __instance.MyInv?.OH?.MyItem;
                bool unarmed = weapon == null || weapon == GameData.PlayerInv?.Empty ||
                    string.IsNullOrWhiteSpace(weapon.ItemName) ||
                    string.Equals(weapon.ItemName, "Empty",
                        StringComparison.OrdinalIgnoreCase);
                bool isTwoHand = mainHand && __instance.MyInv != null &&
                    __instance.MyInv.TwoHandPrimary;
                var wtype = unarmed
                    ? Skills.CombatSkills.WeaponType.HandToHand
                    : Skills.CombatSkills.ClassifyWeapon(weapon, isTwoHand);
                if (wtype == Skills.CombatSkills.WeaponType.Unknown) return;

                float mult = Skills.CombatSkills.GetDamageMultiplier(wtype);
                if (mult > 1f)
                    __result = Mathf.RoundToInt(__result * mult);
            }
            catch { }
        }
    }

    /// <summary>
    /// Apply combat skill damage bonus to bow/archery damage calculations.
    /// Also applies sim player melee bonus (bows are melee-scale for sim purposes).
    /// </summary>
    [HarmonyPatch(typeof(Stats), "CalcBowDamage")]
    public static class Patch_CombatDamageBonus_Bow
    {
        public static void Postfix(Stats __instance, ref int __result)
        {
            try
            {
                if (__result <= 0) return;

                // Sim player bow bonus (uses melee scale since bows are physical)
                if (__instance.Myself != null && Skills.SimPlayerSkills.IsSimPlayer(__instance.Myself))
                {
                    float simMult = Skills.SimPlayerSkills.GetMeleeDamageMultiplier(__instance.Myself);
                    if (simMult > 1f)
                        __result = Mathf.RoundToInt(__result * simMult);
                    return;
                }

                // Player bow bonus
                if (!SkillsPlugin.CfgEnableCombatSkills.Value) return;
                if (__instance.Myself == null) return;
                var pc = GameData.PlayerControl;
                if (pc == null || __instance.Myself != pc) return;

                float mult = Skills.CombatSkills.GetDamageMultiplier(
                    Skills.CombatSkills.WeaponType.Archery);
                if (mult > 1f)
                    __result = Mathf.RoundToInt(__result * mult);
            }
            catch { }
        }
    }

    /// <summary>Apply the Wands combat skill to the game's final wand calculation.</summary>
    [HarmonyPatch(typeof(WandBolt), "CalcDmgBonusWand",
        new System.Type[] { typeof(int), typeof(Character) })]
    public static class Patch_CombatDamageBonus_Wand
    {
        public static void Postfix(Character _caster, ref int __result)
        {
            try
            {
                if (__result <= 0 || _caster != GameData.PlayerControl?.Myself ||
                    _caster.MyStats?.MyInv?.MH?.MyItem?.IsWand != true) return;

                float multiplier = Skills.CombatSkills.GetDamageMultiplier(
                    Skills.CombatSkills.WeaponType.Wands);
                if (multiplier > 1f)
                    __result = Mathf.RoundToInt(__result * multiplier);
            }
            catch { }
        }
    }

    // ═══════════════════════════════════════════════════════════════════
    // MAGIC SKILLS — XP on spell cast
    // ═══════════════════════════════════════════════════════════════════

    /// <summary>
    /// Award magic skill XP when the player casts a spell.
    /// Hooks CastSpell.StartSpell — fires when a spell begins casting.
    /// Classifies the spell by school and awards XP to the matching skill.
    /// </summary>
    [HarmonyPatch(typeof(CastSpell), "StartSpell",
        new System.Type[] { typeof(Spell), typeof(Stats), typeof(float) })]
    public static class Patch_MagicSkillXP
    {
        public static void Postfix(CastSpell __instance, Spell _spell, bool __result)
        {
            if (!__result) return;
            if (!__instance.isPlayer) return;
            try { Skills.MagicSkills.OnSpellCast(_spell); }
            catch { }
        }
    }

    /// <summary>
    /// Also hook the 2-param StartSpell overload — this is what the hotbar uses.
    /// </summary>
    [HarmonyPatch(typeof(CastSpell), "StartSpell",
        new System.Type[] { typeof(Spell), typeof(Stats) })]
    public static class Patch_MagicSkillXP2
    {
        public static void Postfix(CastSpell __instance, Spell _spell, bool __result)
        {
            if (!__result) return;
            if (!__instance.isPlayer) return;
            try { Skills.MagicSkills.OnSpellCast(_spell); }
            catch { }
        }
    }

    // ═══════════════════════════════════════════════════════════════════
    // MAGIC SKILLS — exact spell context and damage/healing bonuses
    // ═══════════════════════════════════════════════════════════════════

    /// <summary>
    /// MagicDamageMe does not receive the originating spell. ResolveSpell is
    /// synchronous, so preserve the exact spell/caster context for only the
    /// duration of that call. The stack-safe state also handles nested procs.
    /// </summary>
    [HarmonyPatch(typeof(SpellVessel), "ResolveSpell")]
    public static class Patch_PlayerSpellDamage
    {
        public struct ContextState
        {
            public Spell PreviousSpell;
            public CastSpell PreviousSource;
        }

        [ThreadStatic] internal static Spell CurrentSpell;
        [ThreadStatic] internal static CastSpell CurrentSource;

        public static void Prefix(Spell ___spell, CastSpell ___SpellSource,
            out ContextState __state)
        {
            __state = new ContextState
            {
                PreviousSpell = CurrentSpell,
                PreviousSource = CurrentSource
            };
            CurrentSpell = ___spell;
            CurrentSource = ___SpellSource;
        }

        public static void Postfix(ContextState __state)
        {
            Restore(__state);
        }

        public static Exception Finalizer(Exception __exception, ContextState __state)
        {
            Restore(__state);
            return __exception;
        }

        private static void Restore(ContextState state)
        {
            CurrentSpell = state.PreviousSpell;
            CurrentSource = state.PreviousSource;
        }
    }

    /// <summary>
    /// Apply Conjuration to every positive physical-damage component produced
    /// by the player's native summoned pet. DamageMe is the shared endpoint for
    /// pet melee, physical abilities, physical procs, and pet-owned DoT ticks.
    /// </summary>
    [HarmonyPatch(typeof(Character), "DamageMe",
        new System.Type[]
        {
            typeof(int), typeof(bool), typeof(GameData.DamageType),
            typeof(Character), typeof(bool), typeof(bool), typeof(int)
        })]
    public static class Patch_ConjurationPetPhysicalDamage
    {
        internal static int ApplicationsForTesting { get; private set; }
        internal static int LastIncomingForTesting { get; private set; }
        internal static int LastBonusForTesting { get; private set; }
        internal static int LastScaledIncomingForTesting { get; private set; }
        internal static int LastScaledBonusForTesting { get; private set; }

        public static void Prefix(ref int _incdmg, Character _attacker,
            ref int _bonusDmg)
        {
            try
            {
                if (!Skills.MagicSkills.IsPlayerSummonedPet(_attacker)) return;
                int incoming = _incdmg;
                int bonus = _bonusDmg;
                float multiplier = Skills.MagicSkills
                    .GetConjurationMultiplier();
                _incdmg = Skills.MagicSkills.ScalePositiveDamage(
                    _incdmg, multiplier);
                _bonusDmg = Skills.MagicSkills.ScalePositiveDamage(
                    _bonusDmg, multiplier);
                ApplicationsForTesting++;
                LastIncomingForTesting = incoming;
                LastBonusForTesting = bonus;
                LastScaledIncomingForTesting = _incdmg;
                LastScaledBonusForTesting = _bonusDmg;
            }
            catch { }
        }

        internal static void ResetProbeForTesting()
        {
            ApplicationsForTesting = 0;
            LastIncomingForTesting = 0;
            LastBonusForTesting = 0;
            LastScaledIncomingForTesting = 0;
            LastScaledBonusForTesting = 0;
        }
    }

    /// <summary>
    /// Marks the window in which Stats.TickEffects applies periodic damage.
    /// A DoT tick calls Character.DamageMe directly and carries no spell
    /// context, so without this marker there is no way to tell a tick apart
    /// from an ordinary melee hit inside the damage patch.
    /// </summary>
    [HarmonyPatch(typeof(Stats), "TickEffects")]
    public static class Patch_ConjurationDotContext
    {
        [ThreadStatic] internal static bool Active;

        public static void Prefix(out bool __state)
        {
            __state = Active;
            Active = true;
        }

        public static void Postfix(bool __state) => Active = __state;

        public static Exception Finalizer(Exception __exception, bool __state)
        {
            Active = __state;
            return __exception;
        }
    }

    /// <summary>
    /// Conjuration covers DoTs as well as pets, but only the pet half was
    /// wired up. Periodic damage resolves through Stats.TickEffects into
    /// Character.DamageMe, which neither the Evocation patch (MagicDamageMe
    /// only) nor the pet patch (summoned attackers only) covers — so a DoT
    /// caster got no return on the skill for most of their damage.
    /// </summary>
    [HarmonyPatch(typeof(Character), "DamageMe",
        new System.Type[]
        {
            typeof(int), typeof(bool), typeof(GameData.DamageType),
            typeof(Character), typeof(bool), typeof(bool), typeof(int)
        })]
    public static class Patch_ConjurationDotDamage
    {
        internal static int ApplicationsForTesting { get; private set; }
        internal static int LastIncomingForTesting { get; private set; }
        internal static int LastScaledForTesting { get; private set; }

        public static void Prefix(ref int _incdmg, bool _fromPlayer,
            Character _attacker, ref int _bonusDmg)
        {
            try
            {
                if (!Patch_ConjurationDotContext.Active || !_fromPlayer ||
                    _incdmg <= 0 || _attacker == null) return;
                // Only the player's own afflictions. Pet-attributed damage is
                // already scaled by Patch_ConjurationPetPhysicalDamage, and
                // scaling here too would compound it.
                if (_attacker != GameData.PlayerControl?.Myself) return;

                int incoming = _incdmg;
                float multiplier = Skills.MagicSkills
                    .GetConjurationMultiplier();
                _incdmg = Skills.MagicSkills.ScalePositiveDamage(
                    _incdmg, multiplier);
                _bonusDmg = Skills.MagicSkills.ScalePositiveDamage(
                    _bonusDmg, multiplier);
                ApplicationsForTesting++;
                LastIncomingForTesting = incoming;
                LastScaledForTesting = _incdmg;
            }
            catch { }
        }

        internal static void ResetProbeForTesting()
        {
            ApplicationsForTesting = 0;
            LastIncomingForTesting = 0;
            LastScaledForTesting = 0;
        }
    }

    [HarmonyPatch(typeof(Character), "MagicDamageMe")]
    public static class Patch_EvocationDamage
    {
        internal static int PetApplicationsForTesting { get; private set; }
        internal static int LastPetIncomingForTesting { get; private set; }
        internal static int LastPetScaledForTesting { get; private set; }

        public static void Prefix(ref int _dmg, bool _fromPlayer,
            Character _attacker, ref int _baseDmg)
        {
            try
            {
                if (_attacker == null || _dmg <= 0) return;

                // Pet spells and magical procs are not player CastSpell calls,
                // so recognize their native ownership before exact-spell logic.
                if (Skills.MagicSkills.IsPlayerSummonedPet(_attacker))
                {
                    int incoming = _dmg;
                    float petMultiplier = Skills.MagicSkills
                        .GetConjurationMultiplier();
                    _dmg = Skills.MagicSkills.ScalePositiveDamage(
                        _dmg, petMultiplier);
                    _baseDmg = Skills.MagicSkills.ScalePositiveDamage(
                        _baseDmg, petMultiplier);
                    PetApplicationsForTesting++;
                    LastPetIncomingForTesting = incoming;
                    LastPetScaledForTesting = _dmg;
                    return;
                }

                // Check if attacker is a sim player
                if (Skills.SimPlayerSkills.IsSimPlayer(_attacker))
                {
                    float simMult = Skills.SimPlayerSkills.GetSpellDamageMultiplier(_attacker);
                    if (simMult > 1f)
                        _dmg = Mathf.RoundToInt(_dmg * simMult);
                    return;
                }

                // Only scale damage produced by this player's exact spell.
                // Wand and unrelated MagicDamageMe calls have their own paths.
                if (!_fromPlayer || _attacker != GameData.PlayerControl?.Myself) return;
                Spell current = Patch_PlayerSpellDamage.CurrentSpell;
                CastSpell source = Patch_PlayerSpellDamage.CurrentSource;
                if (current == null || source == null || !source.isPlayer ||
                    source.MyChar != _attacker) return;

                float mult = 1f;
                var school = Skills.MagicSkills.ClassifySpell(current);
                if (school == Skills.MagicSkills.MagicSchool.Evocation)
                    mult = Skills.MagicSkills.GetSpellDamageMultiplier();
                else if (school == Skills.MagicSkills.MagicSchool.Conjuration)
                    mult = Skills.MagicSkills.GetConjurationMultiplier();

                if (mult > 1f)
                    _dmg = Mathf.RoundToInt(_dmg * mult);
            }
            catch { }
        }

        internal static void ResetPetProbeForTesting()
        {
            PetApplicationsForTesting = 0;
            LastPetIncomingForTesting = 0;
            LastPetScaledForTesting = 0;
        }
    }

    // ═══════════════════════════════════════════════════════════════════
    // MAGIC SKILLS — Alteration bonus (heal amount)
    // ═══════════════════════════════════════════════════════════════════

    [HarmonyPatch(typeof(Stats), "HealMe", new System.Type[] {
        typeof(Spell), typeof(int), typeof(bool), typeof(bool), typeof(Character) })]
    public static class Patch_AlterationHeal
    {
        public static void Prefix(Spell _spell, ref int _amt, Character _source)
        {
            try
            {
                if (_amt <= 0 || _source == null) return;

                // Sim player heals
                if (Skills.SimPlayerSkills.IsSimPlayer(_source))
                {
                    float simMult = Skills.SimPlayerSkills.GetHealMultiplier(_source);
                    if (simMult > 1f)
                        _amt = Mathf.RoundToInt(_amt * simMult);
                    return;
                }

                // Player heals
                if (_source != GameData.PlayerControl?.Myself ||
                    Skills.MagicSkills.ClassifySpell(_spell) !=
                        Skills.MagicSkills.MagicSchool.Alteration) return;
                float mult = Skills.MagicSkills.GetHealMultiplier();
                if (mult > 1f)
                    _amt = Mathf.RoundToInt(_amt * mult);
            }
            catch { }
        }
    }

    // ═════════════════════════════════════════════════════════════════════
    // Intercept whispers to sim players for craft requests
    // ═════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Postfix on UpdateSocialLog.LogAdd — intercepts WTB chat messages
    /// to track them in our WTB system so players can whisper to sell.
    /// </summary>
    [HarmonyPatch(typeof(UpdateSocialLog), "LogAdd", new System.Type[] { typeof(ChatLogLine) })]
    public static class Patch_SimWTBTracker
    {
        public static void Postfix(ChatLogLine _logLine)
        {
            try
            {
                if (!SkillsPlugin.CfgEnableSimWTB.Value) return;
                if (_logLine == null) return;
                if (_logLine.MyLogType != ChatLogLine.LogType.WTB) return;

                // Parse: "SimName shouts: WTB ItemName, offering X gold. Open a trade with me."
                string text = _logLine.MyChatString ?? "";
                int shoutsIdx = text.IndexOf(" shouts: WTB ");
                if (shoutsIdx < 0) return;

                string simName = text.Substring(0, shoutsIdx);
                string remainder = text.Substring(shoutsIdx + 13); // after " shouts: WTB "

                // remainder = "ItemName, offering X gold. Open a trade with me."
                int commaIdx = remainder.IndexOf(", offering ");
                if (commaIdx < 0) return;

                string itemName = remainder.Substring(0, commaIdx);
                string goldPart = remainder.Substring(commaIdx + 11); // after ", offering "

                int goldSpaceIdx = goldPart.IndexOf(' ');
                if (goldSpaceIdx < 0) return;
                string goldStr = goldPart.Substring(0, goldSpaceIdx);

                int goldPrice;
                if (!int.TryParse(goldStr, out goldPrice)) return;

                Skills.SimPlayerTradeskills.OnSimWTBShout(simName, itemName, goldPrice);
            }
            catch { }
        }
    }

    /// <summary>
    /// Prefix on SimTradeWindow.CompleteTrade — intercepts recipe scroll
    /// trades to teach sims new recipes.
    /// </summary>
    [HarmonyPatch(typeof(SimTradeWindow), "CompleteTrade")]
    public static class Patch_SimRecipeTrade
    {
        public static bool Prefix(SimTradeWindow __instance)
        {
            try
            {
                if (Skills.SimPlayerTradeskills.TryHandleWTBTrade(__instance))
                    return false;
                if (Skills.SimPlayerTradeskills.TryHandleRecipeTrade(__instance))
                    return false; // we handled it, skip default CompleteTrade
            }
            catch { }
            return true; // normal trade, let game handle it
        }
    }

}
