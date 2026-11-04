using Lunaris.Config;
using UnityEngine;

namespace ErenshorSkills
{
    /// <summary>
    /// Native Lunaris settings model. Stable property names define the native
    /// options consumed by the mod and the Lunaris configuration registry.
    /// </summary>
    public sealed class ClassicSkillsSettings
    {
        [Config("Skill window key", "Global", "Key to toggle the Classic Skills window.")]
        public KeyCode SkillWindowKey { get; set; } = KeyCode.F8;

        [Config("Show chat messages", "Global", "Show skill-up and usage messages in chat.")]
        public bool ShowChatMessages { get; set; } = true;

        [Config("Show utility action buttons", "Interface", "Show draggable Forage, Bind Wound, and Begging icons with hotkeys and cooldown overlays.")]
        public bool ShowUtilityActionButtons { get; set; } = true;

        [Config("Enable corpse run", "Corpse Run", "Opt in to leaving inventory, equipped gear, cosmetics, aura, charm, and gold on a recoverable corpse when you die.")]
        public bool CorpseRunEnabled { get; set; } = false;

        [Config("Enable Necromancer", "Necromancer", "Add the playable Necromancer class, its spells, armed skeletal pets, and spell merchants.")]
        public bool NecromancerEnabled { get; set; } = true;

        [Config("Enable Monk", "Monk", "Add the playable Monk class, its hand-to-hand techniques, auras, class gear, and martial trainers.")]
        public bool MonkEnabled { get; set; } = true;

        [Config("Maximum skill level", "Global", "Maximum level for all skills (default 180; runtime safety cap 1000).")]
        public int MaxSkillLevel { get; set; } = 180;

        [Config("Enable Fishing", "Skill Toggles", "Enable the Fishing skill.")]
        public bool EnableFishing { get; set; } = true;

        [Config("Enable Foraging", "Skill Toggles", "Enable the Foraging skill (find items while exploring).")]
        public bool EnableForaging { get; set; } = true;

        [Config("Enable Swimming", "Skill Toggles", "Enable the Swimming skill (move faster in water).")]
        public bool EnableSwimming { get; set; } = true;

        [Config("Enable Bind Wound", "Skill Toggles", "Enable the Bind Wound skill (bandage yourself out of combat).")]
        public bool EnableBindWound { get; set; } = true;

        [Config("Enable Meditate", "Skill Toggles", "Enable the Meditate skill (faster mana regen while sitting).")]
        public bool EnableMeditate { get; set; } = true;

        [Config("Enable Food Tolerance", "Skill Toggles", "Enable the Food Tolerance skill (food/drink buff duration).")]
        public bool EnableFoodTolerance { get; set; } = true;

        [Config("Enable Begging", "Skill Toggles", "Enable the Begging skill (beg NPCs for copper).")]
        public bool EnableBegging { get; set; } = true;

        [Config("Enable combat skills", "Skill Toggles", "Enable combat weapon skills (1H Slash, 1H Blunt, Piercing, etc.).")]
        public bool EnableCombatSkills { get; set; } = true;

        [Config("Craft from bags", "Tradeskills", "Allow tradeskill crafting to pull materials from bags in your inventory.")]
        public bool CraftFromBags { get; set; } = false;

        [Config("Craft from bank", "Tradeskills", "Allow tradeskill crafting to pull materials from your bank.")]
        public bool CraftFromBank { get; set; } = false;

        [Config("Enable magic skills", "Skill Toggles", "Enable magic skills (Evocation, Abjuration, Alteration, Conjuration).")]
        public bool EnableMagicSkills { get; set; } = true;

        [Config("Enable SimPlayer skills", "Skill Toggles", "Give sim players virtual combat/magic skill bonuses based on their level and class.")]
        public bool EnableSimPlayerSkills { get; set; } = true;

        [Config("Enable SimCraft", "Skill Toggles", "Allow players to whisper sim players to request crafting. Sim will craft, negotiate price, walk to you, and trade.")]
        public bool EnableSimCraft { get; set; } = true;

        [Config("SimCraft fail cooldown (seconds)", "Skill Toggles", "Cooldown in seconds before a sim player can attempt the same recipe again after a total failure.")]
        [ConfigRange(1f, 3600f)]
        public int SimCraftFailCooldownSeconds { get; set; } = 60;

        [Config("Sim chat minimum interval (seconds)", "Skill Toggles", "Minimum seconds between ambient sim tradeskill/skill chat messages.")]
        [ConfigRange(30f, 86400f)]
        public int SimChatIntervalMinSeconds { get; set; } = 180;

        [Config("Sim chat maximum interval (seconds)", "Skill Toggles", "Maximum seconds between ambient sim tradeskill/skill chat messages.")]
        [ConfigRange(60f, 86400f)]
        public int SimChatIntervalMaxSeconds { get; set; } = 480;

        [Config("Enable Sim WTB", "Skill Toggles", "Allow players to whisper sims to sell items they have shouted WTB for. Sim comes to you and you trade via native window.")]
        public bool EnableSimWTB { get; set; } = true;

        [Config("Enable Sim WTS", "Skill Toggles", "Sims shout real WTS listings for items they can craft. Players can whisper to buy pre-crafted items.")]
        public bool EnableSimWTS { get; set; } = true;

        [Config("Sim WTB/WTS expiry (seconds)", "Skill Toggles", "How long a WTB or WTS listing stays active before expiring (default 5 minutes).")]
        [ConfigRange(10f, 86400f)]
        public int SimWTBExpirySeconds { get; set; } = 300;

        [Config("Catch up combat/magic skills", "Skill Toggles", "On first load, set combat and magic skill levels based on character level and class. Helps pre-existing characters catch up.")]
        public bool CatchUpCombatMagicSkills { get; set; } = false;

        [Config("Catch up utility/tradeskills", "Skill Toggles", "On first load, set utility and tradeskill levels based on character level. Helps pre-existing characters catch up.")]
        public bool CatchUpUtilityTradeskills { get; set; } = false;

        [Config("Force catch-up on next load", "Skill Toggles", "Set to true to force catch-up skill levels on your next login, even if you already have a save file. Resets to false automatically after applying.")]
        public bool ForceCatchUpOnNextLoad { get; set; } = false;

        [Config("Player hit fraction", "AttackSync", "Fraction of swing animation when hit lands.")]
        [ConfigRange(0.1f, 0.9f)]
        public float PlayerHitFraction { get; set; } = 0.45f;

        [Config("NPC hit fraction", "AttackSync", "Fraction of NPC swing when hit sound plays.")]
        [ConfigRange(0.1f, 0.9f)]
        public float NPCHitFraction { get; set; } = 0.40f;

        [Config("Minimum hit delay", "AttackSync", "Minimum hit delay in seconds.")]
        [ConfigRange(0.02f, 0.3f)]
        public float MinHitDelay { get; set; } = 0.08f;

        [Config("Maximum hit delay", "AttackSync", "Maximum hit delay in seconds.")]
        [ConfigRange(0.2f, 2.0f)]
        public float MaxHitDelay { get; set; } = 0.8f;

        [Config("Sync player attacks", "AttackSync", "Enable sync fix for player melee auto-attacks.")]
        public bool SyncPlayerAttacks { get; set; } = true;

        [Config("Sync NPC attacks", "AttackSync", "Enable sync fix for NPC/enemy melee attacks.")]
        public bool SyncNPCAttacks { get; set; } = true;

        [Config("Sync SimPlayer attacks", "AttackSync", "Enable sync fix for SimPlayer (party member) melee attacks.")]
        public bool SyncSimPlayerAttacks { get; set; } = true;

        [Config("Attack sync debug logging", "AttackSync", "Log animation lengths and computed delays.")]
        public bool DebugLogging { get; set; } = false;

        [Config("Forage key", "Keybinds", "Key to attempt foraging.")]
        public KeyCode ForageKey { get; set; } = KeyCode.F9;

        [Config("Bind Wound key", "Keybinds", "Key to bind wounds (bandage self).")]
        public KeyCode BindWoundKey { get; set; } = KeyCode.F10;

        [Config("Beg key", "Keybinds", "Key to beg from targeted NPC.")]
        public KeyCode BegKey { get; set; } = KeyCode.Semicolon;

        [Config("Meditate key", "Keybinds", "Legacy setting retained for config compatibility. Meditation now activates automatically while sitting out of combat.")]
        public KeyCode MeditateKey { get; set; } = KeyCode.M;

        [Config("Bind Wound base heal percent", "Skill Tuning", "Base percentage of max HP healed per Bind Wound use. Scales +0.15% per level (30% at skill 180).")]
        [ConfigRange(0f, 50f)]
        public float BindWoundBaseHealPercent { get; set; } = 3f;

        [Config("Meditate base mana", "Skill Tuning", "Base extra mana per tick while meditating. Scales with skill level.")]
        [ConfigRange(0f, 1000f)]
        public float MeditateBaseMana { get; set; } = 2f;

        [Config("Combat skill damage per level", "Skill Tuning", "Percent bonus damage per combat skill level. At 0.085, level 180 = +15.3% damage.")]
        [ConfigRange(0f, 1f)]
        public float CombatSkillDamagePerLevel { get; set; } = 0.085f;

        [Config("Evocation damage per level", "Skill Tuning", "Bonus spell damage per Evocation level. At 0.0008, level 180 = +14.4%.")]
        [ConfigRange(0f, 0.01f)]
        public float EvocationDamagePerLevel { get; set; } = 0.0008f;

        [Config("Abjuration duration per level", "Skill Tuning", "Bonus buff duration per Abjuration level. At 0.003, level 180 = +54%.")]
        [ConfigRange(0f, 0.02f)]
        public float AbjurationDurationPerLevel { get; set; } = 0.003f;

        [Config("Alteration heal per level", "Skill Tuning", "Bonus heal amount per Alteration level. At 0.0008, level 180 = +14.4%.")]
        [ConfigRange(0f, 0.01f)]
        public float AlterationHealPerLevel { get; set; } = 0.0008f;

        [Config("Conjuration damage per level", "Skill Tuning", "Bonus Conjuration DoT and summoned-pet damage per level. At 0.0008, level 180 = +14.4%.")]
        [ConfigRange(0f, 0.01f)]
        public float ConjurationDamagePerLevel { get; set; } = 0.0008f;

        [Config("Test cooldowns", "Testing", "Set all utility skill cooldowns to 1 second for testing.")]
        public bool TestCooldowns { get; set; } = false;

        [Config("Enable test mode", "Testing", "Enable the controlled /cstest smoke-test and item-grant commands. Leave disabled during normal play.")]
        public bool EnableTestMode { get; set; } = false;

        [Config("Enable debug commands", "Testing", "Enable developer-only chat commands that grant items/points or dump data.")]
        public bool EnableDebugCommands { get; set; } = false;

        [Config("Enable custom log colors", "Chat and Combat Log", "Use the per-message-type colors below in chat and combat logs. Disable this to preserve all native Erenshor colors.")]
        public bool EnableCustomLogColors { get; set; } = false;

        [Config("Show damage types", "Chat and Combat Log", "Append the resolved damage school to damage messages, such as [Physical], [Elemental], or [Void].")]
        public bool ShowDamageTypes { get; set; } = false;

        [Config("Say color", "Chat and Combat Log Colors", "Color for say messages. Use #RRGGBB, #RRGGBBAA, a supported color name, or leave blank to retain that message's native color.")]
        public string LogColorSay { get; set; } = "#F2F2F2";

        [Config("Shout color", "Chat and Combat Log Colors", "Color for shout messages. Use #RRGGBB, #RRGGBBAA, a supported color name, or leave blank to retain that message's native color.")]
        public string LogColorShout { get; set; } = "#FF9D3D";

        [Config("Whisper color", "Chat and Combat Log Colors", "Color for whisper messages. Use #RRGGBB, #RRGGBBAA, a supported color name, or leave blank to retain that message's native color.")]
        public string LogColorWhisper { get; set; } = "#FF75D8";

        [Config("Party color", "Chat and Combat Log Colors", "Color for party messages. Use #RRGGBB, #RRGGBBAA, a supported color name, or leave blank to retain that message's native color.")]
        public string LogColorParty { get; set; } = "#55E6E6";

        [Config("Guild color", "Chat and Combat Log Colors", "Color for guild messages. Use #RRGGBB, #RRGGBBAA, a supported color name, or leave blank to retain that message's native color.")]
        public string LogColorGuild { get; set; } = "#55E0A4";

        [Config("Raid color", "Chat and Combat Log Colors", "Color for raid messages. Use #RRGGBB, #RRGGBBAA, a supported color name, or leave blank to retain that message's native color.")]
        public string LogColorRaid { get; set; } = "#FF8766";

        [Config("Trade / WTB color", "Chat and Combat Log Colors", "Color for trade / wtb messages. Use #RRGGBB, #RRGGBBAA, a supported color name, or leave blank to retain that message's native color.")]
        public string LogColorTradeWTB { get; set; } = "#FFD24A";

        [Config("Your melee damage color", "Chat and Combat Log Colors", "Color for your melee damage messages. Use #RRGGBB, #RRGGBBAA, a supported color name, or leave blank to retain that message's native color.")]
        public string LogColorYourMeleeDamage { get; set; } = "#F2F2F2";

        [Config("Your spell damage color", "Chat and Combat Log Colors", "Color for your spell damage messages. Use #RRGGBB, #RRGGBBAA, a supported color name, or leave blank to retain that message's native color.")]
        public string LogColorYourSpellDamage { get; set; } = "#84C8FF";

        [Config("Other damage color", "Chat and Combat Log Colors", "Color for other damage messages. Use #RRGGBB, #RRGGBBAA, a supported color name, or leave blank to retain that message's native color.")]
        public string LogColorOtherDamage { get; set; } = "#BDBDBD";

        [Config("Damage to you color", "Chat and Combat Log Colors", "Color for damage to you messages. Use #RRGGBB, #RRGGBBAA, a supported color name, or leave blank to retain that message's native color.")]
        public string LogColorDamageToYou { get; set; } = "#FF6666";

        [Config("Your healing color", "Chat and Combat Log Colors", "Color for your healing messages. Use #RRGGBB, #RRGGBBAA, a supported color name, or leave blank to retain that message's native color.")]
        public string LogColorYourHealing { get; set; } = "#66F28A";

        [Config("Other healing color", "Chat and Combat Log Colors", "Color for other healing messages. Use #RRGGBB, #RRGGBBAA, a supported color name, or leave blank to retain that message's native color.")]
        public string LogColorOtherHealing { get; set; } = "#68C982";

        [Config("Healing on you color", "Chat and Combat Log Colors", "Color for healing on you messages. Use #RRGGBB, #RRGGBBAA, a supported color name, or leave blank to retain that message's native color.")]
        public string LogColorHealingOnYou { get; set; } = "#8CFFB7";

        [Config("Spell emotes color", "Chat and Combat Log Colors", "Color for spell emotes messages. Use #RRGGBB, #RRGGBBAA, a supported color name, or leave blank to retain that message's native color.")]
        public string LogColorSpellEmotes { get; set; } = "#CAA8FF";

        [Config("Other spell results color", "Chat and Combat Log Colors", "Color for other spell results messages. Use #RRGGBB, #RRGGBBAA, a supported color name, or leave blank to retain that message's native color.")]
        public string LogColorOtherSpellResults { get; set; } = "#B9A7FF";

        [Config("System messages color", "Chat and Combat Log Colors", "Color for system messages messages. Use #RRGGBB, #RRGGBBAA, a supported color name, or leave blank to retain that message's native color.")]
        public string LogColorSystemMessages { get; set; } = "#FFF176";

        [Config("NPC emotes color", "Chat and Combat Log Colors", "Color for npc emotes messages. Use #RRGGBB, #RRGGBBAA, a supported color name, or leave blank to retain that message's native color.")]
        public string LogColorNPCEmotes { get; set; } = "#D0D0D0";

        [Config("Zone emotes color", "Chat and Combat Log Colors", "Color for zone emotes messages. Use #RRGGBB, #RRGGBBAA, a supported color name, or leave blank to retain that message's native color.")]
        public string LogColorZoneEmotes { get; set; } = "#FFD37A";

        [Config("Battle mechanics color", "Chat and Combat Log Colors", "Color for battle mechanics messages. Use #RRGGBB, #RRGGBBAA, a supported color name, or leave blank to retain that message's native color.")]
        public string LogColorBattleMechanics { get; set; } = "#FF704F";

        [Config("Faction changes color", "Chat and Combat Log Colors", "Color for faction changes messages. Use #RRGGBB, #RRGGBBAA, a supported color name, or leave blank to retain that message's native color.")]
        public string LogColorFactionChanges { get; set; } = "#FFCC66";

        [Config("Target information color", "Chat and Combat Log Colors", "Color for target information messages. Use #RRGGBB, #RRGGBBAA, a supported color name, or leave blank to retain that message's native color.")]
        public string LogColorTargetInfo { get; set; } = "#80E8FF";

        [Config("Loot requests color", "Chat and Combat Log Colors", "Color for loot requests messages. Use #RRGGBB, #RRGGBBAA, a supported color name, or leave blank to retain that message's native color.")]
        public string LogColorLootRequests { get; set; } = "#D5A6FF";

    }
}
