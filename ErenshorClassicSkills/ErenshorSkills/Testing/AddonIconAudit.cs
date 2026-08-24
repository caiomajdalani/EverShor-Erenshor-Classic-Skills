using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace ErenshorSkills.Testing
{
    /// <summary>
    /// Global runtime contract for addon-owned iconography. This validates the
    /// actual Sprite objects shown by inventory, spellbook, skill, aura, gear,
    /// and character-class UI rather than merely counting PNG files.
    /// </summary>
    internal static class AddonIconAudit
    {
        internal static bool Validate(out string detail)
        {
            var errors = new List<string>();
            var entries = new List<KeyValuePair<string, Sprite>>();
            var labels = new HashSet<string>(
                StringComparer.OrdinalIgnoreCase);

            List<Item> items = Items.ItemFactory
                .GetRegisteredItemsForTesting();
            foreach (Item item in items)
            {
                string name = item?.ItemName ?? "";
                if (item == null || string.IsNullOrWhiteSpace(name))
                {
                    errors.Add("custom item registry contains a null or unnamed item");
                    continue;
                }
                Add(entries, labels, "Item: " + name, item.ItemIcon, errors);
            }

            AddMonkEntries(entries, labels, errors);
            AddNecromancerEntries(entries, labels, errors);

            bool pixelsValid = ValidatePixels(entries,
                out string pixelDetail);
            if (!pixelsValid) errors.Add(pixelDetail);

            detail = errors.Count == 0
                ? $"{items.Count} registered custom items plus 77 " +
                  "Monk/Necromancer skill, spell, aura-effect, and class " +
                  $"icons resolve to {entries.Count} present, readable, " +
                  "non-white, exact-pixel-unique runtime sprites"
                : string.Join("; ", errors.Take(16)) +
                  (errors.Count > 16
                      ? $"; ... ({errors.Count} errors)" : "");
            return errors.Count == 0;
        }

        private static void AddMonkEntries(
            List<KeyValuePair<string, Sprite>> entries,
            HashSet<string> labels, List<string> errors)
        {
            Monk.MonkCatalog.TryInitialize();
            int spells = Monk.MonkCatalog.AllSpells.Count;
            int tomes = Monk.MonkCatalog.AllTomes.Count;
            int auras = Monk.MonkAuras.AllSpells.Count;
            int auraItems = Monk.MonkAuras.AllItems.Count;
            int gear = Monk.MonkGear.AllItems.Count;
            if (spells != Monk.MonkSkillData.ExpectedCustomCount)
                errors.Add($"Monk spellbook icon sources {spells}/" +
                    Monk.MonkSkillData.ExpectedCustomCount);
            if (tomes != Monk.MonkSkillData.ExpectedCustomCount)
                errors.Add($"Monk tome item sources {tomes}/" +
                    Monk.MonkSkillData.ExpectedCustomCount);
            if (auras != Monk.MonkAuras.ExpectedCount ||
                auraItems != Monk.MonkAuras.ExpectedCount)
                errors.Add($"Monk aura sources effects={auras}, items=" +
                    $"{auraItems}, expected={Monk.MonkAuras.ExpectedCount}");
            if (gear != Monk.MonkGear.ExpectedCount)
                errors.Add($"Monk gear item sources {gear}/" +
                    Monk.MonkGear.ExpectedCount);

            foreach (Spell spell in Monk.MonkCatalog.AllSpells)
                Add(entries, labels,
                    "Monk spellbook technique: " +
                    (spell?.SpellName ?? "<null>"),
                    spell?.SpellIcon, errors);
            foreach (Spell spell in Monk.MonkAuras.AllSpells)
                Add(entries, labels,
                    "Monk aura effect: " +
                    (spell?.SpellName ?? "<null>"),
                    spell?.SpellIcon, errors);
            Add(entries, labels, "Monk class crest",
                Monk.MonkCatalog.ClassIcon, errors);

            foreach (Item item in Monk.MonkCatalog.AllTomes
                .Concat(Monk.MonkAuras.AllItems)
                .Concat(Monk.MonkGear.AllItems))
            {
                if (item != null && !Items.ItemFactory.IsCustomItem(item))
                    errors.Add("Monk item missing from custom registry: " +
                        item.ItemName);
            }
        }

        private static void AddNecromancerEntries(
            List<KeyValuePair<string, Sprite>> entries,
            HashSet<string> labels, List<string> errors)
        {
            Necromancer.NecromancerCatalog.TryInitialize();
            int spells = Necromancer.NecromancerCatalog.AllSpells.Count;
            int scrolls = Necromancer.NecromancerCatalog.AllScrolls.Count;
            int auras = Necromancer.NecromancerAuras.AllSpells.Count;
            int auraItems = Necromancer.NecromancerAuras.AllItems.Count;
            int gear = Necromancer.NecromancerGear.AllItems.Count;
            if (spells != Necromancer.NecromancerSpellData
                .ExpectedTotalSpellCount)
                errors.Add($"Necromancer spell icon sources {spells}/" +
                    Necromancer.NecromancerSpellData
                        .ExpectedTotalSpellCount);
            if (scrolls != Necromancer.NecromancerSpellData
                .ExpectedLearnableSpellCount)
                errors.Add($"Necromancer scroll item sources {scrolls}/" +
                    Necromancer.NecromancerSpellData
                        .ExpectedLearnableSpellCount);
            if (auras != Necromancer.NecromancerAuras.ExpectedCount ||
                auraItems != Necromancer.NecromancerAuras.ExpectedCount)
                errors.Add($"Necromancer aura sources effects={auras}, " +
                    $"items={auraItems}, expected=" +
                    Necromancer.NecromancerAuras.ExpectedCount);
            if (gear != Necromancer.NecromancerGear.ExpectedCount)
                errors.Add($"Necromancer gear item sources {gear}/" +
                    Necromancer.NecromancerGear.ExpectedCount);

            foreach (Spell spell in Necromancer.NecromancerCatalog.AllSpells)
                Add(entries, labels,
                    "Necromancer spell: " +
                    (spell?.SpellName ?? "<null>"),
                    spell?.SpellIcon, errors);
            foreach (Spell spell in Necromancer.NecromancerAuras.AllSpells)
                Add(entries, labels,
                    "Necromancer aura effect: " +
                    (spell?.SpellName ?? "<null>"),
                    spell?.SpellIcon, errors);
            Add(entries, labels, "Necromancer class crest",
                Necromancer.NecromancerCatalog.ClassIcon, errors);

            foreach (Item item in Necromancer.NecromancerCatalog.AllScrolls
                .Concat(Necromancer.NecromancerAuras.AllItems)
                .Concat(Necromancer.NecromancerGear.AllItems))
            {
                if (item != null && !Items.ItemFactory.IsCustomItem(item))
                    errors.Add(
                        "Necromancer item missing from custom registry: " +
                        item.ItemName);
            }
        }

        private static void Add(
            List<KeyValuePair<string, Sprite>> entries,
            HashSet<string> labels, string label, Sprite sprite,
            List<string> errors)
        {
            if (!labels.Add(label))
            {
                errors.Add("duplicate audit label " + label);
                return;
            }
            entries.Add(new KeyValuePair<string, Sprite>(label, sprite));
        }

        private static bool ValidatePixels(
            IList<KeyValuePair<string, Sprite>> entries,
            out string detail)
        {
            var errors = new List<string>();
            var spriteIds = new Dictionary<int, string>();
            var fingerprints = new Dictionary<ulong, string>();

            foreach (KeyValuePair<string, Sprite> entry in entries)
            {
                Sprite sprite = entry.Value;
                Texture2D texture = sprite?.texture;
                if (sprite == null || texture == null)
                {
                    errors.Add(entry.Key + " has no runtime sprite");
                    continue;
                }

                int spriteId = sprite.GetInstanceID();
                if (spriteIds.TryGetValue(spriteId, out string firstSprite))
                    errors.Add(entry.Key + " reuses the same Sprite as " +
                        firstSprite);
                else
                    spriteIds[spriteId] = entry.Key;

                try
                {
                    Color32[] pixels = texture.GetPixels32();
                    int xMin = Mathf.Clamp(
                        Mathf.RoundToInt(sprite.rect.x), 0,
                        texture.width - 1);
                    int yMin = Mathf.Clamp(
                        Mathf.RoundToInt(sprite.rect.y), 0,
                        texture.height - 1);
                    int width = Mathf.Clamp(
                        Mathf.RoundToInt(sprite.rect.width), 1,
                        texture.width - xMin);
                    int height = Mathf.Clamp(
                        Mathf.RoundToInt(sprite.rect.height), 1,
                        texture.height - yMin);
                    int visible = 0;
                    int nearWhite = 0;
                    int minLuma = 255;
                    int maxLuma = 0;
                    long lumaTotal = 0;
                    long channelSpreadTotal = 0;
                    ulong fingerprint = 1469598103934665603UL;
                    Fingerprint(ref fingerprint, width);
                    Fingerprint(ref fingerprint, height);

                    for (int y = yMin; y < yMin + height; y++)
                    {
                        int row = y * texture.width;
                        for (int x = xMin; x < xMin + width; x++)
                        {
                            Color32 pixel = pixels[row + x];
                            Fingerprint(ref fingerprint, pixel.r);
                            Fingerprint(ref fingerprint, pixel.g);
                            Fingerprint(ref fingerprint, pixel.b);
                            Fingerprint(ref fingerprint, pixel.a);
                            if (pixel.a <= 8) continue;
                            visible++;
                            int luma = (pixel.r * 299 + pixel.g * 587 +
                                pixel.b * 114) / 1000;
                            int channelSpread = Mathf.Max(pixel.r,
                                Mathf.Max(pixel.g, pixel.b)) -
                                Mathf.Min(pixel.r,
                                    Mathf.Min(pixel.g, pixel.b));
                            lumaTotal += luma;
                            channelSpreadTotal += channelSpread;
                            minLuma = Mathf.Min(minLuma, luma);
                            maxLuma = Mathf.Max(maxLuma, luma);
                            if (pixel.r >= 245 && pixel.g >= 245 &&
                                pixel.b >= 245) nearWhite++;
                        }
                    }

                    if (visible < 64)
                        errors.Add(entry.Key + " is blank/transparent");
                    if (maxLuma - minLuma < 24)
                        errors.Add(entry.Key + " is a flat placeholder");
                    if (visible > 0 && nearWhite * 100 >= visible * 95)
                        errors.Add(entry.Key + " is a white placeholder");
                    int area = width * height;
                    bool spellStyle =
                        entry.Key.StartsWith("Monk ability: ",
                            StringComparison.Ordinal) ||
                        entry.Key.StartsWith("Monk aura effect: ",
                            StringComparison.Ordinal) ||
                        entry.Key.StartsWith("Necromancer spell: ",
                            StringComparison.Ordinal) ||
                        entry.Key.StartsWith(
                            "Necromancer aura effect: ",
                            StringComparison.Ordinal);
                    bool itemStyle = entry.Key.StartsWith("Item: ",
                        StringComparison.Ordinal);
                    bool classStyle = entry.Key.EndsWith(" class crest",
                        StringComparison.Ordinal);
                    if (spellStyle && visible * 100 < area * 98)
                        errors.Add(entry.Key +
                            " violates native opaque spell-icon framing");
                    if (itemStyle && visible * 100 > area * 92)
                        errors.Add(entry.Key +
                            " violates native transparent item-icon framing");
                    if (classStyle &&
                        (visible * 100 < area * 55 ||
                         visible * 100 > area * 90))
                        errors.Add(entry.Key +
                            " violates native class-medallion coverage");
                    if (visible > 0 &&
                        lumaTotal / visible < (spellStyle ? 27 : 22))
                        errors.Add(entry.Key +
                            " is too dark for native icon readability");
                    if (classStyle && visible > 0 &&
                        channelSpreadTotal / visible > 8)
                        errors.Add(entry.Key +
                            " is colored instead of a neutral tintable crest");
                    if (fingerprints.TryGetValue(fingerprint,
                        out string firstPixels))
                        errors.Add(entry.Key +
                            " duplicates the exact pixels of " +
                            firstPixels);
                    else
                        fingerprints[fingerprint] = entry.Key;
                }
                catch (Exception ex)
                {
                    errors.Add(entry.Key + " icon is unreadable: " +
                        ex.Message);
                }
            }

            detail = errors.Count == 0
                ? $"{entries.Count} runtime icons have distinct Sprite " +
                  "instances, exact pixel fingerprints, native category " +
                  "alpha framing, readable exposure, and tint-neutral class " +
                  "crests"
                : string.Join("; ", errors.Take(16)) +
                  (errors.Count > 16
                      ? $"; ... ({errors.Count} pixel errors)" : "");
            return errors.Count == 0;
        }

        private static void Fingerprint(ref ulong value, int component)
        {
            value ^= (byte)component;
            value *= 1099511628211UL;
            value ^= (byte)(component >> 8);
            value *= 1099511628211UL;
        }
    }
}
