using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using BepInEx.Configuration;
using HarmonyLib;
using UnityEngine;

namespace ErenshorSkills.Features
{
    /// <summary>
    /// Optional EverQuest-style log presentation.  The feature operates on
    /// ChatLogLine after Erenshor has assigned its normal filter category, so
    /// it does not reroute messages or interfere with native log filters.
    /// </summary>
    internal static class LogDisplayOptions
    {
        private sealed class ColorRule
        {
            internal ChatLogLine.LogType Type;
            internal string Label;
            internal string DisplayName;
            internal string DefaultColor;
            internal ConfigEntry<string> Entry;
        }

        private struct DamageEvent
        {
            internal int Amount;
            internal GameData.DamageType Type;
            internal int Frame;
            internal float Time;
        }

        private const int MaximumDamageEvents = 64;
        private const int MaximumDamageEventAgeFrames = 3;
        private const float MaximumDamageEventAgeSeconds = 0.4f;

        private static readonly List<ColorRule> ColorRules =
            new List<ColorRule>();
        private static readonly List<DamageEvent> DamageEvents =
            new List<DamageEvent>();
        private static readonly HashSet<string> WarnedInvalidColors =
            new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private static readonly HashSet<string> SupportedNamedColors =
            new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                "black", "blue", "cyan", "gray", "green", "grey",
                "lightblue", "magenta", "orange", "purple", "red",
                "white", "yellow"
            };
        private static readonly Regex HexColorPattern = new Regex(
            "^#(?:[0-9a-fA-F]{3}|[0-9a-fA-F]{4}|[0-9a-fA-F]{6}|[0-9a-fA-F]{8})$",
            RegexOptions.Compiled | RegexOptions.CultureInvariant);
        private static readonly Regex RichTextPattern = new Regex(
            "<[^>]+>", RegexOptions.Compiled | RegexOptions.CultureInvariant);
        private static readonly Regex DamageSuffixPattern = new Regex(
            "\\[(?:Physical|Magic|Elemental|Void|Poison)\\]$",
            RegexOptions.Compiled | RegexOptions.CultureInvariant |
            RegexOptions.IgnoreCase);
        private static readonly Regex DamageAmountPattern = new Regex(
            @"\b(?:for|did|take|takes|took|taken|inflict|inflicts)\s+" +
            @"(?<amount>\d+)\b(?=[^0-9\r\n]{0,48}\bdamage\b)|" +
            @"\b(?<amount>\d+)\s+(?:critical\s+)?" +
            @"(?:(?:bleed|spell)\s+)?damage\b",
            RegexOptions.Compiled | RegexOptions.CultureInvariant |
            RegexOptions.IgnoreCase);

        internal static ConfigEntry<bool> EnableCustomColors { get; private set; }
        internal static ConfigEntry<bool> ShowDamageTypes { get; private set; }

        internal static void BindConfig(ConfigFile config)
        {
            EnableCustomColors = config.Bind("Chat and Combat Log",
                "EnableCustomLogColors", false,
                "Use the per-message-type colors below in chat and combat logs. Disable this to preserve all native Erenshor colors.");
            ShowDamageTypes = config.Bind("Chat and Combat Log",
                "ShowDamageTypes", false,
                "Append the resolved damage school to damage messages, such as [Physical], [Elemental], or [Void].");

            ColorRules.Clear();
            BindColor(config, ChatLogLine.LogType.Say, "Say", "#F2F2F2");
            BindColor(config, ChatLogLine.LogType.Shout, "Shout", "#FF9D3D");
            BindColor(config, ChatLogLine.LogType.Whisper, "Whisper", "#FF75D8");
            BindColor(config, ChatLogLine.LogType.Party, "Party", "#55E6E6");
            BindColor(config, ChatLogLine.LogType.Guild, "Guild", "#55E0A4");
            BindColor(config, ChatLogLine.LogType.Raid, "Raid", "#FF8766");
            BindColor(config, ChatLogLine.LogType.WTB, "TradeWTB", "#FFD24A");
            BindColor(config, ChatLogLine.LogType.PlayerHits,
                "YourMeleeDamage", "#F2F2F2");
            BindColor(config, ChatLogLine.LogType.PlayerSpellResults,
                "YourSpellDamage", "#84C8FF");
            BindColor(config, ChatLogLine.LogType.OthersHits,
                "OtherDamage", "#BDBDBD");
            BindColor(config, ChatLogLine.LogType.PlayerGetHit,
                "DamageToYou", "#FF6666");
            BindColor(config, ChatLogLine.LogType.MyHealing,
                "YourHealing", "#66F28A");
            BindColor(config, ChatLogLine.LogType.OtherHealing,
                "OtherHealing", "#68C982");
            BindColor(config, ChatLogLine.LogType.HealsOnMe,
                "HealingOnYou", "#8CFFB7");
            BindColor(config, ChatLogLine.LogType.SpellEmotes,
                "SpellEmotes", "#CAA8FF");
            BindColor(config, ChatLogLine.LogType.SpellResults,
                "OtherSpellResults", "#B9A7FF");
            BindColor(config, ChatLogLine.LogType.SystemMessages,
                "SystemMessages", "#FFF176");
            BindColor(config, ChatLogLine.LogType.NPCEmotes,
                "NPCEmotes", "#D0D0D0");
            BindColor(config, ChatLogLine.LogType.ZoneEmotes,
                "ZoneEmotes", "#FFD37A");
            BindColor(config, ChatLogLine.LogType.BattleMechanicText,
                "BattleMechanics", "#FF704F");
            BindColor(config, ChatLogLine.LogType.FactionHits,
                "FactionChanges", "#FFCC66");
            BindColor(config, ChatLogLine.LogType.TargetInfo,
                "TargetInfo", "#80E8FF");
            BindColor(config, ChatLogLine.LogType.GroupMemberRequestsLoot,
                "LootRequests", "#D5A6FF");
        }

        private static void BindColor(ConfigFile config,
            ChatLogLine.LogType type, string label, string defaultColor)
        {
            ColorRules.Add(new ColorRule
            {
                Type = type,
                Label = label,
                DisplayName = GetDisplayName(label),
                DefaultColor = defaultColor,
                Entry = config.Bind("Chat and Combat Log Colors",
                    "LogColor" + label, defaultColor,
                    $"Color for {DescribeLogType(type)} messages. Use #RRGGBB, #RRGGBBAA, a supported color name, or leave blank to retain that message's native color.")
            });
        }

        private static string GetDisplayName(string label)
        {
            switch (label)
            {
                case "TradeWTB": return "Trade / WTB";
                case "YourMeleeDamage": return "Your melee damage";
                case "YourSpellDamage": return "Your spell damage";
                case "OtherDamage": return "Other damage";
                case "DamageToYou": return "Damage to you";
                case "YourHealing": return "Your healing";
                case "OtherHealing": return "Other healing";
                case "HealingOnYou": return "Healing on you";
                case "SpellEmotes": return "Spell emotes";
                case "OtherSpellResults": return "Other spell results";
                case "SystemMessages": return "System messages";
                case "NPCEmotes": return "NPC emotes";
                case "ZoneEmotes": return "Zone emotes";
                case "BattleMechanics": return "Battle mechanics";
                case "FactionChanges": return "Faction changes";
                case "TargetInfo": return "Target information";
                case "LootRequests": return "Loot requests";
                default: return label;
            }
        }

        private static string DescribeLogType(ChatLogLine.LogType type)
        {
            switch (type)
            {
                case ChatLogLine.LogType.PlayerHits: return "your melee damage";
                case ChatLogLine.LogType.PlayerSpellResults: return "your spell damage";
                case ChatLogLine.LogType.OthersHits: return "damage involving others";
                case ChatLogLine.LogType.PlayerGetHit: return "damage to you";
                case ChatLogLine.LogType.MyHealing: return "your healing";
                case ChatLogLine.LogType.OtherHealing: return "healing by others";
                case ChatLogLine.LogType.HealsOnMe: return "healing received";
                case ChatLogLine.LogType.WTB: return "trade / WTB";
                case ChatLogLine.LogType.GroupMemberRequestsLoot:
                    return "group loot requests";
                default: return type.ToString();
            }
        }

        internal static void Apply(ChatLogLine line)
        {
            if (line == null) return;

            if (EnableCustomColors?.Value == true &&
                TryGetConfiguredColor(line.MyLogType, out string color))
                line.ColorString = color;

            if (ShowDamageTypes?.Value == true)
                TryAppendDamageType(line);
        }

        private static bool TryGetConfiguredColor(ChatLogLine.LogType type,
            out string color)
        {
            foreach (ColorRule rule in ColorRules)
            {
                if ((type & rule.Type) == 0) continue;
                string configured = rule.Entry?.Value;
                if (TryNormalizeColor(configured, out color))
                    return !string.IsNullOrEmpty(color);

                string warningKey = rule.Label + "=" + configured;
                if (WarnedInvalidColors.Add(warningKey))
                    SkillsPlugin.Log?.LogWarning(
                        $"Ignoring invalid log color '{configured}' for {rule.Label}; native color retained.");
                color = null;
                return false;
            }
            color = null;
            return false;
        }

        private static bool TryNormalizeColor(string configured,
            out string normalized)
        {
            normalized = configured?.Trim();
            if (string.IsNullOrEmpty(normalized))
            {
                normalized = null;
                return true;
            }
            if (SupportedNamedColors.Contains(normalized)) return true;
            if (!HexColorPattern.IsMatch(normalized) ||
                !ColorUtility.TryParseHtmlString(normalized, out _))
            {
                normalized = null;
                return false;
            }
            return true;
        }

        internal static void RecordDamage(int amount,
            GameData.DamageType damageType)
        {
            if (ShowDamageTypes?.Value != true || amount <= 0) return;
            CleanupDamageEvents();
            DamageEvents.Add(new DamageEvent
            {
                Amount = amount,
                Type = damageType,
                Frame = Time.frameCount,
                Time = Time.unscaledTime
            });
            if (DamageEvents.Count > MaximumDamageEvents)
                DamageEvents.RemoveRange(0,
                    DamageEvents.Count - MaximumDamageEvents);
        }

        private static void CleanupDamageEvents()
        {
            int minimumFrame = Time.frameCount - MaximumDamageEventAgeFrames;
            float minimumTime = Time.unscaledTime - MaximumDamageEventAgeSeconds;
            DamageEvents.RemoveAll(value => value.Frame < minimumFrame ||
                value.Time < minimumTime);
        }

        private static void TryAppendDamageType(ChatLogLine line)
        {
            const ChatLogLine.LogType damageCategories =
                ChatLogLine.LogType.PlayerHits |
                ChatLogLine.LogType.OthersHits |
                ChatLogLine.LogType.PlayerGetHit |
                ChatLogLine.LogType.SpellEmotes |
                ChatLogLine.LogType.SpellResults |
                ChatLogLine.LogType.PlayerSpellResults;
            if ((line.MyLogType & damageCategories) == 0 ||
                string.IsNullOrWhiteSpace(line.MyChatString) ||
                DamageSuffixPattern.IsMatch(line.MyChatString)) return;

            string plain = RichTextPattern.Replace(line.MyChatString, "");
            Match amountMatch = DamageAmountPattern.Match(plain);
            if (!amountMatch.Success ||
                !int.TryParse(amountMatch.Groups["amount"].Value,
                    out int amount) || amount <= 0) return;

            CleanupDamageEvents();
            for (int index = DamageEvents.Count - 1; index >= 0; index--)
            {
                DamageEvent candidate = DamageEvents[index];
                if (candidate.Amount != amount) continue;
                DamageEvents.RemoveAt(index);
                line.MyChatString = line.MyChatString.TrimEnd() +
                    " [" + candidate.Type + "]";
                return;
            }
        }

        internal static bool RunSelfTest(out string detail)
        {
            var errors = new List<string>();
            if (EnableCustomColors == null || ShowDamageTypes == null)
                errors.Add("feature toggles are not bound");
            if (ColorRules.Count != 23)
                errors.Add($"expected 23 color categories, found {ColorRules.Count}");
            foreach (ColorRule rule in ColorRules)
                if (rule.Entry == null ||
                    !TryNormalizeColor(rule.Entry.Value, out _))
                    errors.Add("invalid color setting for " + rule.Label);

            string[] samples =
            {
                "You hit a target for 17 damage!",
                "Your bolt did 18 damage to a target",
                "A target took 19 CRITICAL damage from Rot",
                "YOU have taken 20 from a damage shield!",
                "You inflict 21 damage upon yourself!",
                "A target takes 22 spell damage!"
            };
            for (int index = 0; index < samples.Length; index++)
            {
                Match match = DamageAmountPattern.Match(samples[index]);
                if (!match.Success ||
                    !int.TryParse(match.Groups["amount"].Value,
                        out int amount) || amount != 17 + index)
                    errors.Add("damage parser rejected sample " + (index + 1));
            }
            if (DamageAmountPattern.IsMatch(
                "Your heal restores 99 points of health."))
                errors.Add("healing text was mistaken for damage");

            detail = errors.Count == 0
                ? "23 configurable categories; six native damage phrasings; native-preserving toggles"
                : string.Join("; ", errors);
            return errors.Count == 0;
        }

        internal static int ColorCategoryCount => ColorRules.Count;

        internal static string GetColorDisplayName(int index)
        {
            return IsValidColorIndex(index)
                ? ColorRules[index].DisplayName : "Unknown category";
        }

        internal static string GetConfiguredColor(int index)
        {
            return IsValidColorIndex(index)
                ? ColorRules[index].Entry?.Value ?? "" : "";
        }

        internal static string GetDefaultColor(int index)
        {
            return IsValidColorIndex(index)
                ? ColorRules[index].DefaultColor : "#FFFFFF";
        }

        internal static bool IsUsingNativeColor(int index)
        {
            return IsValidColorIndex(index) &&
                string.IsNullOrWhiteSpace(GetConfiguredColor(index));
        }

        internal static bool TryGetPickerColor(int index, out Color color)
        {
            color = Color.white;
            if (!IsValidColorIndex(index)) return false;
            string configured = GetConfiguredColor(index);
            string value = string.IsNullOrWhiteSpace(configured)
                ? GetDefaultColor(index) : configured;
            return ColorUtility.TryParseHtmlString(value, out color);
        }

        internal static bool TrySetConfiguredColor(int index, string value,
            out string error)
        {
            error = null;
            if (!IsValidColorIndex(index))
            {
                error = "color category index is out of range";
                return false;
            }
            if (!TryNormalizeColor(value, out string normalized))
            {
                error = "use a supported color name or #RRGGBB";
                return false;
            }
            ColorRules[index].Entry.Value = normalized ?? "";
            return true;
        }

        internal static bool ResetConfiguredColor(int index,
            out string error)
        {
            return TrySetConfiguredColor(index, GetDefaultColor(index),
                out error);
        }

        internal static void ResetAllConfiguredColors()
        {
            foreach (ColorRule rule in ColorRules)
                if (rule.Entry != null)
                    rule.Entry.Value = rule.DefaultColor;
        }

        internal static bool EmitPreviewLines(int index, out string detail)
        {
            if (!IsValidColorIndex(index))
            {
                detail = "color category index is out of range";
                return false;
            }
            ColorRule rule = ColorRules[index];
            ChatLogLine.LogType forced =
                ChatLogLine.LogType.ForcedLineNoFilter;
            UpdateSocialLog.CombatLogAdd(new ChatLogLine(
                "[Log Preview] " + rule.DisplayName + " message",
                rule.Type | forced));
            if (ShowDamageTypes?.Value == true)
            {
                const int amount = 97;
                RecordDamage(amount, GameData.DamageType.Void);
                UpdateSocialLog.CombatLogAdd(new ChatLogLine(
                    "[Log Preview] You hit a target for " + amount +
                    " damage!", ChatLogLine.LogType.PlayerHits | forced));
            }
            detail = ShowDamageTypes?.Value == true
                ? rule.DisplayName + " plus a resolved Void damage sample"
                : rule.DisplayName + " sample";
            return true;
        }

        private static bool IsValidColorIndex(int index)
        {
            return index >= 0 && index < ColorRules.Count &&
                ColorRules[index]?.Entry != null;
        }

        internal static void EmitColorSmokeBatch(int start, int count)
        {
            ChatLogLine.LogType forced =
                ChatLogLine.LogType.ForcedLineNoFilter;
            int end = Mathf.Min(ColorRules.Count, start + count);
            for (int index = Mathf.Max(0, start); index < end; index++)
            {
                ColorRule rule = ColorRules[index];
                UpdateSocialLog.CombatLogAdd(new ChatLogLine(
                    "[Color QA] " + rule.Label + " message",
                    rule.Type | forced));
            }
        }

        internal static void EmitDamageSmokeLines()
        {
            ChatLogLine.LogType forced =
                ChatLogLine.LogType.ForcedLineNoFilter;
            int amount = 71;
            foreach (GameData.DamageType type in Enum.GetValues(
                typeof(GameData.DamageType)))
            {
                RecordDamage(amount, type);
                UpdateSocialLog.CombatLogAdd(new ChatLogLine(
                    "[Damage QA] You hit a target for " + amount +
                    " damage!", ChatLogLine.LogType.PlayerHits | forced));
                amount++;
            }
        }
    }

    // Keep the constructor signature out of Harmony's enum-valued attribute.
    // Lunaris 0.1.9 performs one legacy-plugin metadata pass without its
    // Harmony Cecil resolver; enum arguments make that pass try to resolve the
    // addon's HarmonyX 2.9 reference before Lunaris supplies Harmony 2.13.
    [HarmonyPatch]
    internal static class Patch_ChatLogLineDisplayOptions
    {
        private static System.Reflection.MethodBase TargetMethod() =>
            AccessTools.Constructor(typeof(ChatLogLine), new Type[]
            {
                typeof(string), typeof(ChatLogLine.LogType), typeof(string)
            });

        private static void Postfix(ChatLogLine __instance)
        {
            LogDisplayOptions.Apply(__instance);
        }
    }

    [HarmonyPatch(typeof(Stats), nameof(Stats.ReduceHP))]
    internal static class Patch_LogDamageTypeCapture
    {
        private static void Prefix(int _dmg, GameData.DamageType _dmgType)
        {
            LogDisplayOptions.RecordDamage(_dmg, _dmgType);
        }
    }
}
