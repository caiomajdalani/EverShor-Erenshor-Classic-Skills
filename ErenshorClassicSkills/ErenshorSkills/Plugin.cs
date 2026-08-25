using System;
using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using HarmonyLib;
using UnityEngine;

namespace ErenshorSkills
{
    [BepInPlugin(PluginGuid, PluginName, PluginVersion)]
    public class SkillsPlugin : BaseUnityPlugin
    {
        public const string PluginGuid = "com.erenshor.classicskills";
        public const string PluginName = "Erenshor Classic Skills";
        public const string PluginVersion = "1.8.2";

        internal static ManualLogSource Log;
        internal static SkillsPlugin Instance;
        private Harmony _harmony;
        private bool _recoverNativeItemsAfterLiveEnable;

        // ── Global Config ───────────────────────────────────────────────
        internal static ConfigEntry<KeyCode> CfgSkillWindowKey;
        internal static ConfigEntry<bool> CfgShowChatMessages;
        internal static ConfigEntry<bool> CfgShowUtilityActionButtons;
        internal static ConfigEntry<bool> CfgEnableCorpseRun;
        internal static ConfigEntry<bool> CfgEnableNecromancer;
        internal static ConfigEntry<bool> CfgEnableMonk;
        internal static ConfigEntry<int> CfgGlobalMaxLevel;

        // ── Per-skill toggles ───────────────────────────────────────────
        internal static ConfigEntry<bool> CfgEnableFishing;
        internal static ConfigEntry<bool> CfgEnableForaging;
        internal static ConfigEntry<bool> CfgEnableSwimming;
        // Sense Heading removed
        internal static ConfigEntry<bool> CfgEnableBindWound;
        internal static ConfigEntry<bool> CfgEnableMeditate;
        // Safe Fall removed
        internal static ConfigEntry<bool> CfgEnableFoodTolerance;
        internal static ConfigEntry<bool> CfgEnableBegging;

        // ── Combat skills ───────────────────────────────────────────────
        internal static ConfigEntry<bool> CfgEnableCombatSkills;
        internal static ConfigEntry<float> CfgCombatSkillDamagePerLevel;

        // ── Magic skills ────────────────────────────────────────────
        internal static ConfigEntry<bool> CfgEnableMagicSkills;
        internal static ConfigEntry<bool> CfgEnableSimPlayerSkills;
        internal static ConfigEntry<bool> CfgEnableSimCraft;
        internal static ConfigEntry<int> CfgSimCraftFailCooldown;
        internal static ConfigEntry<int> CfgSimChatIntervalMin;
        internal static ConfigEntry<int> CfgSimChatIntervalMax;
        internal static ConfigEntry<bool> CfgEnableSimWTB;
        internal static ConfigEntry<bool> CfgEnableSimWTS;
        internal static ConfigEntry<int> CfgSimWTBExpiry;
        internal static ConfigEntry<bool> CfgCatchUpCombatMagic;
        internal static ConfigEntry<bool> CfgCatchUpUtilityTrade;
        internal static ConfigEntry<bool> CfgForceCatchUp;
        internal static ConfigEntry<bool> CfgCraftFromBags;
        internal static ConfigEntry<bool> CfgCraftFromBank;
        internal static ConfigEntry<float> CfgEvocationDamagePerLevel;
        internal static ConfigEntry<float> CfgAbjurationDurationPerLevel;
        internal static ConfigEntry<float> CfgAlterationHealPerLevel;
        internal static ConfigEntry<float> CfgConjurationDamagePerLevel;

        // ── Skill-specific config ───────────────────────────────────────
        internal static ConfigEntry<KeyCode> CfgForageKey;
        internal static ConfigEntry<KeyCode> CfgBindWoundKey;
        // Sense Heading removed
        internal static ConfigEntry<KeyCode> CfgBegKey;
        internal static ConfigEntry<KeyCode> CfgMeditateKey;
        internal static ConfigEntry<float> CfgBindWoundBaseHealPercent;
        internal static ConfigEntry<float> CfgMeditateBaseMana;
        // Safe Fall removed
        internal static ConfigEntry<bool> CfgTestCooldowns;
        internal static ConfigEntry<bool> CfgEnableTestMode;
        internal static ConfigEntry<bool> CfgEnableDebugCommands;

        internal static int ExpectedPatchClassCount { get; private set; }
        internal static int AppliedPatchClassCount { get; private set; }
        internal static int FailedPatchClassCount { get; private set; }

        private void Awake()
        {
            Instance = this;
            Log = Logger;

            bool pathsValid = RuntimePaths.Validate(out string pathDetail);
            if (pathsValid)
                Log.LogInfo($"Runtime paths: {pathDetail}");
            else
                Log.LogWarning($"Runtime path validation failed: {pathDetail}");

            BindConfig();
            ValidateConfig();

            _harmony = new Harmony(PluginGuid);
            PatchAllSafe();

            // Close skills window on scene change (logout, zone change)
            UnityEngine.SceneManagement.SceneManager.sceneLoaded += OnSceneLoaded;

            Log.LogInfo($"{PluginName} v{PluginVersion} loaded!");
            Log.LogInfo($"  Press {CfgSkillWindowKey.Value} to open the Skills window.");
            Log.LogInfo($"  Type /skills in chat for a summary.");
        }

        private void Start()
        {
            Log.LogInfo($"Classic Skills: Start() called. GO={gameObject.name}, active={gameObject.activeSelf}");

            // Load sim-learned recipes
            Skills.SimPlayerTradeskills.LoadSimRecipes();

            UnityEngine.SceneManagement.Scene activeScene =
                UnityEngine.SceneManagement.SceneManager.GetActiveScene();
            bool activeWorld = activeScene.IsValid() &&
                activeScene.name != "Menu" && activeScene.name != "LoadScene" &&
                GameData.PlayerInv?.StoredSlots != null &&
                GameData.CurrentCharacterSlot != null;
            bool catalogWasInitialized = Items.ItemFactory.Initialized;
            if (!catalogWasInitialized && GameData.ItemDB != null)
                Items.ItemFactory.Initialize(GameData.ItemDB);
            Necromancer.NecromancerCatalog.TryInitialize();
            Monk.MonkCatalog.TryInitialize();
            if (activeWorld)
            {
                Necromancer.NecromancerCatalog.RestoreKnownSpellsAfterHotLoad();
                Monk.MonkCatalog.RestoreKnownSkillsAfterHotLoad();
            }
            _recoverNativeItemsAfterLiveEnable = activeWorld &&
                !catalogWasInitialized && Items.ItemFactory.Initialized;
            Items.ItemFactory.SetHotLoadRecoveryPending(
                _recoverNativeItemsAfterLiveEnable);

            // Initialize uGUI-based skills window
            if (gameObject.GetComponent<SkillsUI>() == null)
                gameObject.AddComponent<SkillsUI>();
            if (gameObject.GetComponent<MenuBarButton>() == null)
                gameObject.AddComponent<MenuBarButton>();
            if (gameObject.GetComponent<UI.LogDisplaySettingsUI>() == null)
                gameObject.AddComponent<UI.LogDisplaySettingsUI>();
            if (gameObject.GetComponent<UI.ExtraSettingsMenuBarButton>() == null)
                gameObject.AddComponent<UI.ExtraSettingsMenuBarButton>();
            if (gameObject.GetComponent<UtilityActionBar>() == null)
                gameObject.AddComponent<UtilityActionBar>();
            if (gameObject.GetComponent<Testing.AddonTestMode>() == null)
                gameObject.AddComponent<Testing.AddonTestMode>();
            if (gameObject.GetComponent<Testing.UiAutomationBridge>() == null)
                gameObject.AddComponent<Testing.UiAutomationBridge>();

            if (CfgEnableTestMode?.Value == true)
                Log.LogWarning(
                    "TEST MODE IS ENABLED. Disable Testing.EnableTestMode after controlled verification.");

            // BepInEx normally creates us before the character scene loads, but
            // Lunaris can enable or hot-reload a legacy plugin while the player
            // is already in the world.  In that case there is no future
            // sceneLoaded event to bootstrap the current character, so run the
            // same guarded scene initialization against the active scene now.
            // The real sceneLoaded callback remains subscribed for later zone
            // and character changes.
            if (activeScene.IsValid())
                OnSceneLoaded(activeScene,
                    UnityEngine.SceneManagement.LoadSceneMode.Single);
            if (activeWorld)
                Items.SimPlayerCraftedItems.ReconcileLoadedSimPlayers();

        }

        /// <summary>
        /// Patch each class individually so one failure doesn't kill the rest.
        /// </summary>
        private void PatchAllSafe()
        {
            var patchTypes = new System.Collections.Generic.List<System.Type>
            {
                typeof(Patches.Patch_SaveOnInventory),
                typeof(Patches.Patch_DoFishing),
                // Fishing cast time reduction removed
                typeof(Patches.Patch_FishingCatchBonus),
                typeof(Patches.Patch_Swimming),
                // Patch_UseConsumable removed — ambiguous match on Stats.AddStatusEffect
                // Our Patch_UseCustomConsumable handles custom food items instead
                typeof(Patches.Patch_MeditationTick),
                typeof(Patches.Patch_UseCustomConsumable),
                typeof(Patches.Patch_SwimSpeed),
                typeof(Patches.Patch_BuffDuration),
                // Combat XP + Attack Sync Fix (merged)
                typeof(Patches.Patch_PerformAttacks),
                typeof(Patches.Patch_MeleeXP),
                typeof(Patches.Patch_WandXP),
                typeof(Patches.Patch_ArcheryXP),
                typeof(Patches.Patch_NPCMeleeSync),
                typeof(Patches.Patch_ChatCommands),
                typeof(Features.Patch_ChatLogLineDisplayOptions),
                typeof(Features.Patch_LogDamageTypeCapture),
                typeof(Patches.Patch_CorpseRunDeath),
                typeof(Patches.Patch_CorpseRunNativeSaveResult),
                typeof(Patches.Patch_CorpseRunLootPriority),
                typeof(Necromancer.Patch_NecromancerClassDatabase),
                typeof(Necromancer.Patch_NecromancerSpellDatabase),
                typeof(Necromancer.Patch_NecromancerStarterAuraMerchant),
                typeof(Necromancer.Patch_NecromancerGearLootTables),
                typeof(Necromancer.Patch_NecromancerAscensionDatabase),
                typeof(Necromancer.Patch_NecromancerAscensionEffects),
                typeof(Necromancer.Patch_NecromancerAscensionSpent),
                typeof(Necromancer.Patch_NecromancerAscensionClassTab),
                typeof(Necromancer.Patch_NecromancerCharacterCreationStart),
                typeof(Patches.Patch_CustomClassSelectSlot),
                typeof(Patches.Patch_CustomClassSelectBackupSlot),
                typeof(Patches.Patch_CustomClassPlay),
                typeof(Patches.Patch_CustomClassPlayAlternate),
                typeof(Patches.Patch_CustomClassNativeSave),
                typeof(Patches.Patch_CustomClassRawSave),
                typeof(Necromancer.Patch_NecromancerStartingItems),
                typeof(Necromancer.Patch_NecromancerPlayerClassIcon),
                typeof(Necromancer.Patch_NecromancerRaidClassIcon),
                typeof(Necromancer.Patch_NecromancerCast),
                typeof(Necromancer.Patch_NecromancerCastStartVfx),
                typeof(Necromancer.Patch_NecromancerSpellResolve),
                typeof(Necromancer.Patch_NecromancerStatusVfx),
                typeof(Necromancer.Patch_NecromancerPetTrade),
                typeof(Necromancer.Patch_NecromancerPetTradeSnapshot),
                typeof(Necromancer.Patch_NecromancerPetTradeCancel),
                typeof(Necromancer.Patch_NecromancerPetDeath),
                typeof(Necromancer.Patch_NecromancerPetTaunt),
                typeof(Necromancer.Patch_NecromancerPetDismiss),
                typeof(Necromancer.Patch_NecromancerPetWeaponAttack),
                typeof(Necromancer.Patch_NecromancerPetBowDelivery),
                typeof(Necromancer.Patch_NecromancerPetStatCurve),
                typeof(Necromancer.Patch_NecromancerFeignAggroOn),
                typeof(Necromancer.Patch_NecromancerFeignForceAggro),
                typeof(Necromancer.Patch_NecromancerFeignManageAggro),
                typeof(Necromancer.Patch_NecromancerSharedSkillMouseUse),
                typeof(Necromancer.Patch_NecromancerSharedSkillGamepadUse),
                typeof(Necromancer.Patch_NecromancerSharedSkillTooltip),
                typeof(Monk.Patch_MonkClassDatabase),
                typeof(Monk.Patch_MonkSkillDatabase),
                typeof(Monk.Patch_MonkSpellDatabase),
                typeof(Monk.Patch_MonkStarterAuraMerchant),
                typeof(Monk.Patch_MonkAscensionDatabase),
                typeof(Monk.Patch_MonkAscensionEffects),
                typeof(Monk.Patch_MonkAscensionSpent),
                typeof(Monk.Patch_MonkAscensionClassTab),
                typeof(Monk.Patch_MonkCharacterCreationStart),
                typeof(Monk.Patch_MonkStartingItems),
                typeof(Monk.Patch_MonkPlayerClassIcon),
                typeof(Monk.Patch_MonkRaidClassIcon),
                typeof(Monk.Patch_MonkSkillMouseUse),
                typeof(Monk.Patch_MonkSkillGamepadUse),
                typeof(Monk.Patch_MonkSkillTooltip),
                typeof(Monk.Patch_MonkUseSkill),
                // Routes technique spells into PrepareSpellUse. Without this
                // registration every technique cast fell through to the native
                // spell path: CastAtk casting animation, mana spent, zero
                // damage or healing, and no VFX — the class simply did not
                // function, while every internal QA check still passed because
                // they invoke the runtime directly.
                typeof(Monk.Patch_MonkCastTechnique),
                typeof(Monk.Patch_MonkDamageRules),
                typeof(Monk.Patch_MonkFeignAggroOn),
                typeof(Monk.Patch_MonkFeignForceAggro),
                typeof(Monk.Patch_MonkFeignManageAggro),
                typeof(Monk.Patch_MonkOpenHandDamage),
                typeof(Monk.Patch_MonkAutoAttackAnimation),
                typeof(Stations.Patch_OpenForge),
                typeof(Stations.Patch_ZoneLoad_Stations),
                typeof(Stations.Patch_VendorLoadWindow),
                typeof(Stations.Patch_VendorPurchase),
                typeof(Stations.Patch_VendorSellStack),
                typeof(Patches.Patch_PreventBagSplit),
                typeof(Patches.Patch_BagStoreOnRightClick),
                typeof(Items.Patch_SimTradeBagComplete),
                typeof(Items.Patch_SimTradeBagCancel),
                typeof(Items.Patch_VendorBagSale),
                typeof(Items.Patch_GlobalBankBagIntegrity),
                typeof(Items.Patch_GlobalBankCustomItemDisplay),
                typeof(Patches.Patch_FoodToleranceXP),
                typeof(Patches.Patch_FoodToleranceDuration4),
                typeof(Items.Patch_ItemDatabase_Init),
                typeof(Items.Patch_ItemDatabase_Awake),
                typeof(Items.Patch_GetItemByID),
                typeof(Patches.Patch_ClassAuraNativeSave),
                typeof(Patches.Patch_ClassAuraEquipmentLoad),
                typeof(Items.Patch_GetCustomBuffById),
                typeof(Items.Patch_ModularParts_AddonBracerOrder),
                typeof(Patches.Patch_TooltipInject),
                typeof(Patches.Patch_SpellTooltip),
                typeof(Patches.Patch_SpellBookTooltip),
                typeof(Patches.Patch_HotbarSpellTooltip),
                typeof(Patches.Patch_CombatDamageBonus_Melee),
                typeof(Patches.Patch_CombatDamageBonus_Bow),
                typeof(Patches.Patch_CombatDamageBonus_Wand),
                typeof(Patches.Patch_RecipeDrop),
                typeof(Patches.Patch_MeatDrop),
                typeof(Patches.Patch_MiningNodeDrop),
                typeof(Patches.Patch_EndgameMaterialDrop),
                typeof(Patches.Patch_MagicSkillXP),
                typeof(Patches.Patch_MagicSkillXP2),
                typeof(Patches.Patch_ConjurationPetPhysicalDamage),
                typeof(Patches.Patch_ConjurationDotContext),
                typeof(Patches.Patch_ConjurationDotDamage),
                typeof(Patches.Patch_EvocationDamage),
                typeof(Patches.Patch_PlayerSpellDamage),
                typeof(Patches.Patch_AlterationHeal),
                typeof(Patches.Patch_SimWTBTracker),
                typeof(Patches.Patch_SimRecipeTrade),
                typeof(Items.Patch_SimTradeCraftedConsumables),
                typeof(Items.Patch_SimPlayerCraftedUpgradeInterest),
                typeof(Items.Patch_SimPlayerCraftedConsumableSpellFilter),
            };

            // The DPS-benchmark instrumentation hooks the hottest methods in
            // the game: Stats.TickEffects (every Stats, every tick) and all
            // four Character damage entry points (every damage event, every
            // character in the zone). They no-op unless a benchmark run is
            // active, but the Harmony wrappers stay in the call path forever.
            // Only install them when QA test mode is actually enabled.
            if (CfgEnableTestMode?.Value == true)
            {
                patchTypes.AddRange(new[]
                {
                    typeof(Testing.Patch_Level35DpsGameManagerSaveGuard),
                    typeof(Testing.Patch_Level35DpsGameDataSaveGuard),
                    typeof(Testing.Patch_Level35DpsPhysicalIsolation),
                    typeof(Testing.Patch_Level35DpsBleedIsolation),
                    typeof(Testing.Patch_Level35DpsMagicIsolation),
                    typeof(Testing.Patch_Level35DpsEnvironmentIsolation),
                    typeof(Testing.Patch_Level35DpsStatusIsolation),
                    typeof(Testing.Patch_Level35DpsStatusTickAttribution),
                    typeof(Testing.Patch_Level35DpsSpellResolveAttribution),
                    typeof(Testing.Patch_Level35DpsWandBoltAttribution),
                });
                Log.LogInfo(
                    "Test mode: DPS-benchmark instrumentation patches enabled.");
            }

            int ok = 0, fail = 0;
            ExpectedPatchClassCount = patchTypes.Count;
            foreach (var t in patchTypes)
            {
                try
                {
                    _harmony.PatchAll(t);
                    ok++;
                }
                catch (Exception ex)
                {
                    fail++;
                    Log.LogWarning($"Patch {t.Name} failed (non-fatal): {ex.Message}");
                }
            }
            AppliedPatchClassCount = ok;
            FailedPatchClassCount = fail;
            Log.LogInfo($"Harmony: {ok} patches applied, {fail} skipped.");
        }

        private void BindConfig()
        {
            // ── Global ──────────────────────────────────────────────────
            CfgSkillWindowKey = Config.Bind("Global", "SkillWindowKey", KeyCode.F8,
                "Key to toggle the Classic Skills window.");
            CfgShowChatMessages = Config.Bind("Global", "ShowChatMessages", true,
                "Show skill-up and usage messages in chat.");
            CfgShowUtilityActionButtons = Config.Bind("Interface",
                "ShowUtilityActionButtons", true,
                "Show draggable Forage, Bind Wound, and Begging icons with hotkeys and cooldown overlays.");
            Features.LogDisplayOptions.BindConfig(Config);
            // Lunaris's legacy ConfigFile wrapper keys entries by key name only,
            // without the BepInEx section. Two generic "Enabled" entries would
            // therefore alias one another and make Necromancer follow the
            // corpse-run default. Keep legacy BepInEx names for existing configs,
            // but use unambiguous keys in Lunaris's plugin-options store.
            string corpseRunEnabledKey = RuntimePaths.IsLunaris
                ? "CorpseRunEnabled" : "Enabled";
            string necromancerEnabledKey = RuntimePaths.IsLunaris
                ? "NecromancerEnabled" : "Enabled";
            string monkEnabledKey = RuntimePaths.IsLunaris
                ? "MonkEnabled" : "Enabled";
            CfgEnableCorpseRun = Config.Bind("Corpse Run", corpseRunEnabledKey, false,
                "Opt in to leaving inventory, equipped gear, cosmetics, aura, charm, and gold on a recoverable corpse when you die.");
            CfgEnableNecromancer = Config.Bind("Necromancer", necromancerEnabledKey, true,
                "Add the playable Necromancer class, its spells, armed skeletal pets, and spell merchants.");
            CfgEnableMonk = Config.Bind("Monk", monkEnabledKey, true,
                "Add the playable Monk class, its hand-to-hand techniques, auras, class gear, and martial trainers.");
            CfgGlobalMaxLevel = Config.Bind("Global", "MaxSkillLevel", 180,
                "Maximum level for all skills (1-180).");

            // ── Skill Toggles ───────────────────────────────────────────
            CfgEnableFishing = Config.Bind("Skill Toggles", "EnableFishing", true,
                "Enable the Fishing skill.");
            CfgEnableForaging = Config.Bind("Skill Toggles", "EnableForaging", true,
                "Enable the Foraging skill (find items while exploring).");
            CfgEnableSwimming = Config.Bind("Skill Toggles", "EnableSwimming", true,
                "Enable the Swimming skill (move faster in water).");
            // Sense Heading removed
            CfgEnableBindWound = Config.Bind("Skill Toggles", "EnableBindWound", true,
                "Enable the Bind Wound skill (bandage yourself out of combat).");
            CfgEnableMeditate = Config.Bind("Skill Toggles", "EnableMeditate", true,
                "Enable the Meditate skill (faster mana regen while sitting).");
            // Safe Fall removed
            CfgEnableFoodTolerance = Config.Bind("Skill Toggles", "EnableFoodTolerance", true,
                "Enable the Food Tolerance skill (food/drink buff duration).");
            CfgEnableBegging = Config.Bind("Skill Toggles", "EnableBegging", true,
                "Enable the Begging skill (beg NPCs for copper).");
            CfgEnableCombatSkills = Config.Bind("Skill Toggles", "EnableCombatSkills", true,
                "Enable combat weapon skills (1H Slash, 1H Blunt, Piercing, etc.).");
            CfgCraftFromBags = Config.Bind("Tradeskills", "CraftFromBags", false,
                "Allow tradeskill crafting to pull materials from bags in your inventory.");
            CfgCraftFromBank = Config.Bind("Tradeskills", "CraftFromBank", false,
                "Allow tradeskill crafting to pull materials from your bank.");

            CfgEnableMagicSkills = Config.Bind("Skill Toggles", "EnableMagicSkills", true,
                "Enable magic skills (Evocation, Abjuration, Alteration, Conjuration).");
            CfgEnableSimPlayerSkills = Config.Bind("Skill Toggles", "EnableSimPlayerSkills", true,
                "Give sim players virtual combat/magic skill bonuses based on their level and class.");
            CfgEnableSimCraft = Config.Bind("Skill Toggles", "EnableSimCraft", true,
                "Allow players to whisper sim players to request crafting. Sim will craft, negotiate price, walk to you, and trade.");
            CfgSimCraftFailCooldown = Config.Bind("Skill Toggles", "SimCraftFailCooldownSeconds", 60,
                "Cooldown in seconds before a sim player can attempt the same recipe again after a total failure.");
            CfgSimChatIntervalMin = Config.Bind("Skill Toggles", "SimChatIntervalMinSeconds", 180,
                "Minimum seconds between ambient sim tradeskill/skill chat messages.");
            CfgSimChatIntervalMax = Config.Bind("Skill Toggles", "SimChatIntervalMaxSeconds", 480,
                "Maximum seconds between ambient sim tradeskill/skill chat messages.");
            CfgEnableSimWTB = Config.Bind("Skill Toggles", "EnableSimWTB", true,
                "Allow players to whisper sims to sell items they've shouted WTB for. Sim comes to you and you trade via native window.");
            CfgEnableSimWTS = Config.Bind("Skill Toggles", "EnableSimWTS", true,
                "Sims shout real WTS listings for items they can craft. Players can whisper to buy pre-crafted items.");
            CfgSimWTBExpiry = Config.Bind("Skill Toggles", "SimWTBExpirySeconds", 300,
                "How long a WTB or WTS listing stays active before expiring (default 5 minutes).");
            CfgCatchUpCombatMagic = Config.Bind("Skill Toggles", "CatchUpCombatMagicSkills", false,
                "On first load, set combat and magic skill levels based on character level and class. Helps pre-existing characters catch up.");
            CfgCatchUpUtilityTrade = Config.Bind("Skill Toggles", "CatchUpUtilityTradeskills", false,
                "On first load, set utility and tradeskill levels based on character level. Helps pre-existing characters catch up.");
            CfgForceCatchUp = Config.Bind("Skill Toggles", "ForceCatchUpOnNextLoad", false,
                "Set to true to force catch-up skill levels on your next login, even if you already have a save file. Resets to false automatically after applying.");

            // ── Attack Sync Fix (merged) ────────────────────────────────
            Patches.AttackSyncConfig.Init(Config);

            // ── Keybinds ────────────────────────────────────────────────
            CfgForageKey = Config.Bind("Keybinds", "ForageKey", KeyCode.F9,
                "Key to attempt foraging.");
            CfgBindWoundKey = Config.Bind("Keybinds", "BindWoundKey", KeyCode.F10,
                "Key to bind wounds (bandage self).");
            // Sense Heading removed
            CfgBegKey = Config.Bind("Keybinds", "BegKey", KeyCode.Semicolon,
                "Key to beg from targeted NPC.");
            CfgMeditateKey = Config.Bind("Keybinds", "MeditateKey", KeyCode.M,
                "Legacy setting retained for config compatibility. Meditation now activates automatically while sitting out of combat.");

            // ── Skill Tuning ────────────────────────────────────────────
            CfgBindWoundBaseHealPercent = Config.Bind("Skill Tuning", "BindWoundBaseHealPercent", 3f,
                "Base percentage of max HP healed per Bind Wound use. Scales +0.15% per level (30% at skill 180).");
            CfgMeditateBaseMana = Config.Bind("Skill Tuning", "MeditateBaseMana", 2f,
                "Base extra mana per tick while meditating. Scales with skill level.");
            // Safe Fall removed
            // Safe Fall removed
            CfgCombatSkillDamagePerLevel = Config.Bind("Skill Tuning", "CombatSkillDamagePerLevel", 0.085f,
                "Percent bonus damage per combat skill level. At 0.085, level 180 = +15.3% damage.");

            // ── Magic Skill Tuning ──────────────────────────────────
            CfgEvocationDamagePerLevel = Config.Bind("Skill Tuning", "EvocationDamagePerLevel", 0.0008f,
                "Bonus spell damage per Evocation level. At 0.0008, level 180 = +14.4%.");
            CfgAbjurationDurationPerLevel = Config.Bind("Skill Tuning", "AbjurationDurationPerLevel", 0.003f,
                "Bonus buff duration per Abjuration level. At 0.003, level 180 = +54%.");
            CfgAlterationHealPerLevel = Config.Bind("Skill Tuning", "AlterationHealPerLevel", 0.0008f,
                "Bonus heal amount per Alteration level. At 0.0008, level 180 = +14.4%.");
            CfgConjurationDamagePerLevel = Config.Bind("Skill Tuning", "ConjurationDamagePerLevel", 0.0008f,
                "Bonus Conjuration DoT and summoned-pet damage per level. At 0.0008, level 180 = +14.4%.");

            // ── Testing ─────────────────────────────────────────────────
            CfgTestCooldowns = Config.Bind("Testing", "TestCooldowns", false,
                "Set all utility skill cooldowns to 1 second for testing.");
            CfgEnableTestMode = Config.Bind("Testing", "EnableTestMode", false,
                "Enable the controlled /cstest smoke-test and item-grant commands. Leave disabled during normal play.");
            CfgEnableDebugCommands = Config.Bind("Testing", "EnableDebugCommands", false,
                "Enable developer-only chat commands that grant items/points or dump data.");
        }

        private void ValidateConfig()
        {
            ClampConfig(CfgGlobalMaxLevel, 1, 1000);
            ClampConfig(CfgSimCraftFailCooldown, 1, 3600);
            ClampConfig(CfgSimChatIntervalMin, 30, 86400);
            ClampConfig(CfgSimChatIntervalMax, 60, 86400);
            if (CfgSimChatIntervalMax.Value < CfgSimChatIntervalMin.Value)
            {
                Log.LogWarning("Sim chat maximum interval was below its minimum; values were aligned.");
                CfgSimChatIntervalMax.Value = CfgSimChatIntervalMin.Value;
            }
            ClampConfig(CfgSimWTBExpiry, 10, 86400);
            ClampConfig(CfgBindWoundBaseHealPercent, 0f, 50f, 3f);
            ClampConfig(CfgMeditateBaseMana, 0f, 1000f, 2f);
            ClampConfig(CfgCombatSkillDamagePerLevel, 0f, 1f, 0.085f);
            ClampConfig(CfgEvocationDamagePerLevel, 0f, 0.01f, 0.0008f);
            ClampConfig(CfgAbjurationDurationPerLevel, 0f, 0.02f, 0.003f);
            ClampConfig(CfgAlterationHealPerLevel, 0f, 0.01f, 0.0008f);
            ClampConfig(CfgConjurationDamagePerLevel, 0f, 0.01f, 0.0008f);
        }

        private static void ClampConfig(ConfigEntry<int> entry, int minimum, int maximum)
        {
            if (entry == null) return;
            int clamped = Mathf.Clamp(entry.Value, minimum, maximum);
            if (clamped == entry.Value) return;
            Log.LogWarning($"Config {entry.Definition.Key} was outside its safe range; clamped to {clamped}.");
            entry.Value = clamped;
        }

        private static void ClampConfig(ConfigEntry<float> entry, float minimum,
            float maximum, float fallback)
        {
            if (entry == null) return;
            float current = entry.Value;
            float clamped = float.IsNaN(current) || float.IsInfinity(current)
                ? fallback : Mathf.Clamp(current, minimum, maximum);
            if (clamped.Equals(current)) return;
            Log.LogWarning($"Config {entry.Definition.Key} was outside its safe range; clamped to {clamped}.");
            entry.Value = clamped;
        }

        private static int _updateCount = 0;
        private static float _lastUpdateErrorLog = -60f;

        private void Update()
        {
            try
            {
                _updateCount++;
                // The saved custom-class identity is authoritative throughout
                // play. Repair any native or third-party class reassignment
                // before class-specific runtime logic executes.
                Patches.CustomClassIdentity.EnforceCurrent(
                    "runtime invariant", allowAbilityRecovery: false,
                    recalculate: true);
                // Never process mod hotkeys while the player is typing in chat.
                // This mirrors Erenshor's own input handling and prevents custom
                // binds such as P or M from opening windows mid-message.
                bool canUseHotkeys = false;
                try
                {
                    canUseHotkeys = SkillsSaveManager.HasLoaded &&
                        !GameData.PlayerTyping && !GameData.InCharSelect &&
                        !AddonInputState.PointerOverAddon &&
                        !IsEscapeMenuOpen;
                }
                catch { }

                if (canUseHotkeys && Input.GetKeyDown(CfgSkillWindowKey.Value))
                {
                    Log.LogInfo("Classic Skills: F8 pressed!");
                    SkillsUI.ToggleWindow();
                }

                // ── Active skill keybinds ───────────────────────────────────
                if (canUseHotkeys && CfgEnableForaging.Value && Input.GetKeyDown(CfgForageKey.Value))
                    Skills.ForagingSkill.TryForage();

                if (canUseHotkeys && CfgEnableBindWound.Value && Input.GetKeyDown(CfgBindWoundKey.Value))
                    Skills.BindWoundSkill.TryBindWound();

                // Sense Heading removed — game has built-in map/compass

                if (canUseHotkeys && CfgEnableBegging.Value && Input.GetKeyDown(CfgBegKey.Value))
                    Skills.BeggingSkill.TryBeg();

                // Ambient sim player tradeskill chat
                if (SkillsSaveManager.HasLoaded)
                {
                    SkillsSaveManager.FlushIfDue();
                    Skills.SimPlayerTradeskills.TryAmbientChat();
                    Skills.SimPlayerTradeskills.CleanupExpiredOffers();
                    Features.CorpseRunSystem.Tick();
                    Necromancer.NecromancerRuntime.Tick();
                    Monk.MonkRuntime.Tick();
                }

                // Periodically reset custom item values to prevent inflation
                // (every ~30 seconds = ~1800 frames at 60fps)
                if (_updateCount % 1800 == 0)
                    Items.ItemFactory.ResetInflatedItemValues();

                // Escape key closes our trade UI if it's open
                if (Input.GetKeyDown(KeyCode.Escape) && UI.SimCraftTradeUI.IsOpen)
                    UI.SimCraftTradeUI.Hide();
            }
            catch (Exception ex)
            {
                if (Time.unscaledTime - _lastUpdateErrorLog >= 10f)
                {
                    _lastUpdateErrorLog = Time.unscaledTime;
                    Log.LogWarning($"Update error (rate-limited): {ex}");
                }
            }
        }

        internal static bool IsEscapeMenuOpen
        {
            get
            {
                try
                {
                    return GameData.GM?.EscapeMenu != null &&
                        (GameData.GM.EscapeMenu.activeSelf ||
                            GameData.GM.EscapeMenu.activeInHierarchy);
                }
                catch
                {
                    return false;
                }
            }
        }

        private void OnDestroy()
        {
            try { StopAllCoroutines(); } catch { }
            try { AddonInputState.Reset(); } catch { }
            try { Skills.SimPlayerTradeskills.OnSceneChange(); } catch { }
            // Save on plugin destroy (game quit)
            try
            {
                Items.ItemFactory.SaveCustomInventory();
                SkillsSaveManager.Save();
                Necromancer.NecromancerPetEquipment.Save();
            }
            catch { }
            try { UI.TradeskillWindow.Shutdown(); } catch { }
            try { UI.SimCraftTradeUI.Shutdown(); } catch { }
            try { UI.LogDisplaySettingsUI.Shutdown(); } catch { }
            try { UI.NativeUiStyle.Reset(); } catch { }
            try { Features.CorpseRunSystem.Shutdown(); } catch { }
            try { Necromancer.NecromancerCatalog.ResetRuntimeReferences(); } catch { }
            try { Monk.MonkCatalog.ResetRuntimeReferences(); } catch { }
            try { Items.BagWindow.Shutdown(); } catch { }
            try { Stations.CraftingStations.Shutdown(); } catch { }
            _harmony?.UnpatchSelf();
            UnityEngine.SceneManagement.SceneManager.sceneLoaded -= OnSceneLoaded;
            if (ReferenceEquals(Instance, this)) Instance = null;
        }

        private void OnSceneLoaded(UnityEngine.SceneManagement.Scene scene, UnityEngine.SceneManagement.LoadSceneMode mode)
        {
            // Scene-bound SimCraft and delayed-load coroutines must not carry stale
            // Character/Transform references into the next zone or character.
            StopAllCoroutines();
            AddonInputState.Reset();
            string name = scene.name;
            Necromancer.NecromancerCatalog.TryInitialize();
            Monk.MonkCatalog.TryInitialize();
            try { Features.CorpseRunSystem.OnSceneLoaded(name); } catch { }
            if (name == "Menu" || name == "LoadScene")
            {
                // Close skills window when returning to menu or loading screen
                if (SkillsUI.IsWindowVisible)
                    SkillsUI.ToggleWindow();

                // Save ONLY if the character name still matches the current player.
                // This prevents saving stale data during character switches.
                if (SkillsSaveManager.HasLoaded)
                {
                    try
                    {
                        Items.ItemFactory.SaveCustomInventory();
                        SkillsSaveManager.Save();
                    }
                    catch { }
                }
            }

            // On ANY scene change, clean up active SimCraft state
            // to prevent stale UI/coroutines persisting across zones
            try
            {
                if (UI.SimCraftTradeUI.IsOpen)
                    UI.SimCraftTradeUI.Hide();
                Skills.SimPlayerTradeskills.OnSceneChange();
            }
            catch { }

            if (name == "Menu" || name == "LoadScene")
            {
                // Clear all stale data IMMEDIATELY so it can't leak
                try { Necromancer.NecromancerPetEquipment.Save(); } catch { }
                Necromancer.NecromancerCatalog.ResetRuntimeReferences();
                Monk.MonkCatalog.ResetRuntimeReferences();
                SkillsSaveManager.Reset();
            }
            else
            {
                // Any non-menu scene: check if we need to load
                if (!SkillsSaveManager.HasLoaded)
                {
                    Log.LogInfo($"Scene '{name}' loaded, HasLoaded=false — starting DelayedCharacterLoad");
                    StartCoroutine(DelayedCharacterLoad());
                }
                else
                {
                    // Verify the loaded character still matches (zone change within same character)
                    try
                    {
                        string currentPlayer = GameData.PlayerStats?.MyName;
                        if (!string.IsNullOrEmpty(currentPlayer) &&
                            !string.IsNullOrEmpty(SkillsSaveManager.CharName) &&
                            SkillsSaveManager.Sanitize(currentPlayer) != SkillsSaveManager.CharName)
                        {
                            Log.LogWarning($"Character mismatch on zone change: loaded='{SkillsSaveManager.CharName}' but player='{currentPlayer}'. Reloading.");
                            SkillsSaveManager.Reset();
                            StartCoroutine(DelayedCharacterLoad());
                        }
                    }
                    catch { }
                }
                Necromancer.NecromancerVendors.OnSceneLoaded(name);
                Monk.MonkVendors.OnSceneLoaded(name);
                Items.SimPlayerCraftedItems.ReconcileLoadedSimPlayers();
            }
        }

        private System.Collections.IEnumerator DelayedCharacterLoad()
        {
            // Wait for player stats to be available
            string charName = "";
            for (int i = 0; i < 300; i++)
            {
                yield return null;
                try
                {
                    var stats = GameData.PlayerStats;
                    if (stats != null)
                    {
                        charName = stats.MyName;
                        if (!string.IsNullOrEmpty(charName)) break;
                    }
                }
                catch { }
            }

            if (!string.IsNullOrEmpty(charName) && !SkillsSaveManager.HasLoaded)
            {
                SkillsSaveManager.Load(charName);
                if (!SkillsSaveManager.HasLoaded)
                {
                    Log.LogError($"Skills could not be loaded safely for '{charName}'.");
                    yield break;
                }
                if (_recoverNativeItemsAfterLiveEnable)
                {
                    Items.ItemFactory.RecoverMissingItemsAfterHotEnable();
                    _recoverNativeItemsAfterLiveEnable =
                        !Items.ItemFactory.HotLoadRecoveryComplete;
                }
                Patches.ClassAuraPersistence.RestoreAfterCharacterLoad();
                FreshCharacterState.ClearIneligibleLoadedAura(out _);
                Features.CorpseRunSystem.OnCharacterLoaded();
                Necromancer.NecromancerPetEquipment.Load();
                Log.LogInfo($"Skills loaded for '{charName}' (character switch)");

                // Apply catch-up for pre-existing characters on first load,
                // or when ForceCatchUpOnNextLoad is enabled
                try
                {
                    bool isFirstLoad = SkillsSaveManager.Data.LastKnownPlayerLevel == 0;
                    bool forceIt = CfgForceCatchUp.Value;

                    if (isFirstLoad || forceIt)
                    {
                        int pLevel = GameData.PlayerStats?.Level ?? 0;
                        string pClass = GameData.PlayerStats?.CharacterClass?.ClassName ?? "";
                        if (pLevel > 1 && !string.IsNullOrEmpty(pClass))
                        {
                            SkillsSaveManager.Data.ApplyCatchUpLevels(pLevel, pClass);
                            SkillsSaveManager.Save();
                        }

                        // Auto-reset the force flag so it doesn't re-apply every login
                        if (forceIt)
                        {
                            CfgForceCatchUp.Value = false;
                            Log.LogInfo("ForceCatchUpOnNextLoad reset to false.");
                        }
                    }
                }
                catch (System.Exception ex)
                {
                    Log.LogWarning($"Catch-up skill application failed: {ex.Message}");
                }
            }
        }

        private void OnApplicationQuit()
        {
            try
            {
                Items.ItemFactory.SaveCustomInventory();
                SkillsSaveManager.Save();
                Necromancer.NecromancerPetEquipment.Save();
                Log.LogInfo("Saved skills on application quit.");
            }
            catch { }
        }

    }

    /// <summary>
    /// Tracks pointer focus over addon windows without borrowing Erenshor's
    /// PlayerTyping flag. Borrowing that global flag could cancel real chat input.
    /// </summary>
    internal static class AddonInputState
    {
        private static int _pointerBlocks;
        internal static bool PointerOverAddon => _pointerBlocks > 0;
        internal static void Enter() { if (_pointerBlocks < int.MaxValue) _pointerBlocks++; }
        internal static void Exit() { if (_pointerBlocks > 0) _pointerBlocks--; }
        internal static void Reset() { _pointerBlocks = 0; }
    }
}
