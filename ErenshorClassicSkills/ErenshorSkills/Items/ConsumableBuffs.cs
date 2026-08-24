using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using UnityEngine;

namespace ErenshorSkills.Items
{
    /// <summary>
    /// CONSUMABLE BUFF SYSTEM
    ///
    /// Creates Spell ScriptableObjects for food/drink items so they
    /// apply real stat buffs when right-clicked (consumed). Parses
    /// the recipe description text to extract stats and duration,
    /// then creates a matching Spell with those values.
    ///
    /// The game's native UseConsumable() → StartSpell() handles
    /// the actual buff application, duration tracking, and removal.
    /// </summary>
    public static class ConsumableBuffs
    {
        private static Dictionary<string, Spell> _buffSpells
            = new Dictionary<string, Spell>(StringComparer.OrdinalIgnoreCase);

        /// <summary>
        /// Create a buff spell from a consumable's description and attach
        /// it to the item so right-click applies the buff.
        /// Call this after item registration for all consumable items.
        /// </summary>
        public static void AttachBuffToItem(Item item, string description)
        {
            if (item == null || string.IsNullOrEmpty(description)) return;
            // Skip non-consumable items
            if (item.RequiredSlot != Item.SlotType.General) return;
            // Skip items that already have an effect
            if (item.ItemEffectOnClick != null) return;
            // Only process items with stat descriptions
            if (!HasStatInfo(description)) return;

            try
            {
                var spell = CreateBuffSpell(item.ItemName, description);
                if (spell != null)
                {
                    item.ItemEffectOnClick = spell;
                    item.SpellCastTime = 0.5f; // Quick cast
                    item.Disposable = true;    // Consumed on use
                    spell.SpellIcon = item.ItemIcon;
                    string category = ItemFactory.ConsumableBuffs.TryGetValue(
                        item.ItemName, out var metadata)
                        ? metadata.category : null;
                    RegisterBuffTemplate(item.ItemName, spell, category);
                }
            }
            catch (Exception ex)
            {
                SkillsPlugin.Log.LogWarning(
                    $"ConsumableBuffs: Failed for {item.ItemName}: {ex.Message}");
            }
        }

        internal static void RegisterBuffTemplate(string itemName, Spell spell,
            string category = null)
        {
            if (string.IsNullOrWhiteSpace(itemName) || spell == null) return;
            if (string.IsNullOrEmpty(spell.Id))
                spell.Id = "CS_Buff_" + itemName.Replace(" ", "_");
            if (!string.IsNullOrWhiteSpace(category))
                spell.SpellName = $"{itemName} [CS_{category}]";
            _buffSpells[itemName] = spell;
        }

        /// <summary>
        /// Resolve a deterministic custom buff ID when Erenshor restores saved
        /// status effects. The persisted duration is supplied by the game.
        /// </summary>
        internal static Spell GetBuffSpellById(string id)
        {
            if (string.IsNullOrEmpty(id)) return null;
            foreach (var spell in _buffSpells.Values)
                if (spell != null && string.Equals(spell.Id, id,
                    StringComparison.OrdinalIgnoreCase))
                    return spell;
            return null;
        }

        internal static int RegisteredBuffCountForTesting => _buffSpells.Count;

        /// <summary>Check if a description contains stat/buff information.</summary>
        private static bool HasStatInfo(string desc)
        {
            string lower = desc.ToLowerInvariant();
            return lower.Contains("str") || lower.Contains("end") ||
                   lower.Contains("dex") || lower.Contains("agi") ||
                   lower.Contains("int") || lower.Contains("wis") ||
                   lower.Contains("cha") || lower.Contains("all stats") ||
                   lower.Contains("resist") || lower.Contains("regen") ||
                   lower.Contains("haste") || lower.Contains("hp") ||
                   lower.Contains("mana") || lower.Contains("restore");
        }

        /// <summary>
        /// Parse a consumable description and create a Spell with matching stats.
        /// Examples:
        ///   "+5 Str, +3 Fire Resist for 10 min."
        ///   "+8 all stats for 12 min."
        ///   "+10 Str, +10 End for 15 min."
        /// </summary>
        private static Spell CreateBuffSpell(string itemName, string desc)
        {
            var spell = ScriptableObject.CreateInstance<Spell>();
            spell.Id = "CS_Buff_" + itemName.Replace(" ", "_");
            spell.SpellName = itemName;
            spell.SpellDesc = desc;
            spell.SelfOnly = true;
            // Set spell type to Beneficial buff
            spell.Type = Spell.SpellType.Beneficial;
            // Set spell line via reflection to avoid enum access issues
            try
            {
                var lf = typeof(Spell).GetField("Line");
                if (lf != null) lf.SetValue(spell, Enum.ToObject(lf.FieldType, 29)); // Global_Buff
            }
            catch { }
            spell.Cooldown = 1f;
            spell.ManaCost = 0;
            spell.SpellChargeTime = 0f;
            spell.InflictOnSelf = true;
            spell.SelfOnly = true;
            spell.InstantEffect = false;
            spell.SpellRange = 0f;
            spell.StatusEffectMessageOnPlayer = "You feel nourished.";

            // Initialize lists to prevent NullReferenceException in StartSpell
            spell.ChargeVariations = new System.Collections.Generic.List<UnityEngine.AudioClip>();
            spell.UsedBy = new System.Collections.Generic.List<Class>();

            // Parse duration — game uses ticks where displayed = ticks * 3 seconds
            int durationMin = ParseDuration(desc);
            float durationSec = durationMin * 60f;
            spell.SpellDurationInTicks = Mathf.Max(1, Mathf.RoundToInt(durationSec / 3f));

            // Zero all stats first
            spell.Str = 0; spell.End = 0; spell.Dex = 0;
            spell.Agi = 0; spell.Int = 0; spell.Wis = 0;
            spell.Cha = 0; spell.HP = 0; spell.Mana = 0;
            spell.MR = 0; spell.ER = 0; spell.PR = 0; spell.VR = 0;
            spell.AC = 0;

            string lower = desc.ToLowerInvariant();

            // Parse "all stats" first
            var allMatch = Regex.Match(lower, @"\+(\d+)\s*all\s*stats");
            if (allMatch.Success)
            {
                int val = int.Parse(allMatch.Groups[1].Value);
                spell.Str = val; spell.End = val; spell.Dex = val;
                spell.Agi = val; spell.Int = val; spell.Wis = val;
                spell.Cha = val;
            }

            // Parse individual stats: "+5 Str", "+8 End", etc.
            ParseAndSetStat(lower, @"str(?:ength)?", ref spell.Str);
            ParseAndSetStat(lower, @"end(?:urance)?", ref spell.End);
            ParseAndSetStat(lower, @"dex(?:terity)?", ref spell.Dex);
            ParseAndSetStat(lower, @"agi(?:lity)?", ref spell.Agi);
            ParseAndSetStat(lower, @"int(?:elligence)?", ref spell.Int);
            ParseAndSetStat(lower, @"wis(?:dom)?", ref spell.Wis);
            ParseAndSetStat(lower, @"cha(?:risma)?", ref spell.Cha);

            // Parse resists: "+8 Fire Resist" etc.
            ParseResist(lower, "(?:fire/cold|fire|cold|elemental)", ref spell.ER);
            ParseResist(lower, "magic", ref spell.MR);
            ParseResist(lower, "poison", ref spell.PR);
            ParseResist(lower, "void", ref spell.VR);

            // "all resists"
            var allRes = Regex.Match(lower, @"\+(\d+)\s*all\s*resist");
            if (allRes.Success)
            {
                int val = int.Parse(allRes.Groups[1].Value);
                spell.MR = Mathf.Max(spell.MR, val);
                spell.ER = Mathf.Max(spell.ER, val);
                spell.PR = Mathf.Max(spell.PR, val);
                spell.VR = Mathf.Max(spell.VR, val);
            }

            // Parse HP/Mana regen. HP regeneration is periodic healing, not
            // Spell.HP (which changes maximum HP in Erenshor).
            var hpRegen = Regex.Match(lower,
                @"\+(\d+)\s*hp(?:\s*/\s*(?:mp|mana))?\s*regen");
            if (hpRegen.Success)
                spell.TargetHealing = int.Parse(hpRegen.Groups[1].Value);

            var mpRegen = Regex.Match(lower,
                @"\+(\d+)\s*(?:hp\s*/\s*)?(?:mp|mana)\s*regen");
            if (mpRegen.Success)
                spell.Mana = int.Parse(mpRegen.Groups[1].Value);

            if (lower.Contains("restores small hp over time"))
                spell.TargetHealing = Mathf.Max(1, spell.TargetHealing);
            if (lower.Contains("restores small mana over time") ||
                lower.Contains("faster mana regen"))
                spell.Mana = Mathf.Max(1, spell.Mana);

            var haste = Regex.Match(lower, @"\+(\d+)\s*%?\s*haste");
            if (haste.Success)
                spell.Haste = int.Parse(haste.Groups[1].Value);

            if (spell.TargetHealing > 0)
                spell.MyDamageType = GameData.DamageType.Physical;

            // Check we actually set something
            if (spell.Str == 0 && spell.End == 0 && spell.Dex == 0 &&
                spell.Agi == 0 && spell.Int == 0 && spell.Wis == 0 &&
                spell.Cha == 0 && spell.HP == 0 && spell.Mana == 0 &&
                spell.MR == 0 && spell.ER == 0 && spell.PR == 0 &&
                spell.VR == 0 && spell.TargetHealing == 0 && spell.Haste == 0f)
            {
                UnityEngine.Object.Destroy(spell);
                return null;
            }

            return spell;
        }

        /// <summary>Parse "+N stat" from description.</summary>
        private static void ParseAndSetStat(string lower, string statPattern, ref int field)
        {
            // Match abbreviations and full names, e.g. "+5 Str" or
            // "+5 Strength".
            var match = Regex.Match(lower, @"\+(\d+)\s*" + statPattern + @"\b");
            if (match.Success)
            {
                int val = int.Parse(match.Groups[1].Value);
                if (val > field) field = val; // Don't override "all stats" if higher
            }
        }

        /// <summary>Parse "+N type Resist" from description.</summary>
        private static void ParseResist(string lower, string type, ref int field)
        {
            var match = Regex.Match(lower, @"\+(\d+)\s*" + type + @"\s*resist");
            if (match.Success)
                field = Mathf.Max(field, int.Parse(match.Groups[1].Value));
        }

        /// <summary>Parse duration in minutes from description. Default 5.</summary>
        private static int ParseDuration(string desc)
        {
            var match = Regex.Match(desc.ToLowerInvariant(), @"(\d+)\s*min");
            if (match.Success && int.TryParse(match.Groups[1].Value, out int minutes))
                return Mathf.Clamp(minutes, 1, 1440);
            return 5; // Default 5 minutes
        }
    }

    /// <summary>
    /// SpellDB only knows Resources-backed spells. Resolve our deterministic
    /// consumable templates so active Food/Drink buffs survive save and reload.
    /// </summary>
    [HarmonyLib.HarmonyPatch(typeof(SpellDB), "GetSpellByID")]
    public static class Patch_GetCustomBuffById
    {
        public static void Postfix(ref Spell __result, string _ID)
        {
            if (__result != null || string.IsNullOrEmpty(_ID) ||
                !_ID.StartsWith("CS_Buff_", StringComparison.OrdinalIgnoreCase))
                return;
            __result = ConsumableBuffs.GetBuffSpellById(_ID);
        }
    }
}
