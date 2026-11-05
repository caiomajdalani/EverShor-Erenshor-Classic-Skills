using System;
using Lunaris;
using Lunaris.Config;
using HarmonyLib;
using UnityEngine;

namespace ErenshorSkills
{
    [LunarisPluginAttribute(PluginName, PluginVersion, "KrautchanPro", "Adds classic skills, tradeskills, SimCraft, and the Monk and Necromancer classes to Erenshor.")]
    [LunarisPermissionAttribute(LunarisPermission.Harmony)]
    public class SkillsPlugin : LunarisPlugin
    {
        public const string PluginGuid = "com.erenshor.classicskills";
        public const string PluginName = "EverShor - Classic Skills, Classes and Features";
        public const string PluginVersion = "1.9.2";

        internal static ModLogger Log;
        private IConfigHandle<ClassicSkillsSettings> _settingsHandle;
        internal static ClassicSkillsSettings Settings { get; private set; }
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

        [LunarisCommandAttribute("skills", "Show your Classic Skills levels and progress.")]
        public static void CommandSkills(string arguments)
        {
            Patches.Patch_ChatCommands.ShowSkillOverviewFromLunaris();
        }

        [LunarisCommandAttribute("extrasettings", "Open the Classic Skills chat and combat log settings.")]
        public static void CommandExtraSettings(string arguments)
        {
            UI.LogDisplaySettingsUI.Open();
        }

        private void Awake()
        {
            Instance = this;
            Log = new ModLogger(Logging);

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
            Features.CustomClassSimPlayers.ReconcileCurrentPopulation();
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

            // Native Lunaris can enable or hot-reload a plugin while the player
            // is already in the world. In that case there is no future
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
                typeof(Patches.Patch_StatsTickEffectsCrashGuard),
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
                typeof(Patches.Patch_FateOfTheFallenCharacterCreationPager),
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
                typeof(Items.Patch_CustomClassSkillBookMouseUse),
                typeof(Items.Patch_CustomClassSkillBookGamepadUse),
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
                typeof(Features.Patch_CustomSimPlayerCategorization),
                typeof(Features.Patch_CustomSimPlayerClassLoad),
                typeof(Features.Patch_CustomSimPlayerSkillLoad),
                typeof(Features.Patch_CustomSimPlayerSave),
                typeof(Features.Patch_CustomSimPlayerDelete),
                typeof(Features.Patch_CustomSimPlayerChatter),
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
            // Register the complete settings model through Lunaris's native API.
            // Property names remain stable within this native configuration model.
            _settingsHandle = Config.Register<ClassicSkillsSettings>();
            Settings = _settingsHandle.Get();
            if (Settings == null)
                throw new InvalidOperationException("Lunaris returned a null ClassicSkillsSettings instance.");

            CfgSkillWindowKey = CreateConfigEntry(nameof(Settings.SkillWindowKey), () => Settings.SkillWindowKey, v => Settings.SkillWindowKey = v);
            CfgShowChatMessages = CreateConfigEntry(nameof(Settings.ShowChatMessages), () => Settings.ShowChatMessages, v => Settings.ShowChatMessages = v);
            CfgShowUtilityActionButtons = CreateConfigEntry(nameof(Settings.ShowUtilityActionButtons), () => Settings.ShowUtilityActionButtons, v => Settings.ShowUtilityActionButtons = v);
            CfgEnableCorpseRun = CreateConfigEntry(nameof(Settings.CorpseRunEnabled), () => Settings.CorpseRunEnabled, v => Settings.CorpseRunEnabled = v);
            CfgEnableNecromancer = CreateConfigEntry(nameof(Settings.NecromancerEnabled), () => Settings.NecromancerEnabled, v => Settings.NecromancerEnabled = v);
            CfgEnableMonk = CreateConfigEntry(nameof(Settings.MonkEnabled), () => Settings.MonkEnabled, v => Settings.MonkEnabled = v);
            CfgGlobalMaxLevel = CreateConfigEntry(nameof(Settings.MaxSkillLevel), () => Settings.MaxSkillLevel, v => Settings.MaxSkillLevel = v);

            CfgEnableFishing = CreateConfigEntry(nameof(Settings.EnableFishing), () => Settings.EnableFishing, v => Settings.EnableFishing = v);
            CfgEnableForaging = CreateConfigEntry(nameof(Settings.EnableForaging), () => Settings.EnableForaging, v => Settings.EnableForaging = v);
            CfgEnableSwimming = CreateConfigEntry(nameof(Settings.EnableSwimming), () => Settings.EnableSwimming, v => Settings.EnableSwimming = v);
            CfgEnableBindWound = CreateConfigEntry(nameof(Settings.EnableBindWound), () => Settings.EnableBindWound, v => Settings.EnableBindWound = v);
            CfgEnableMeditate = CreateConfigEntry(nameof(Settings.EnableMeditate), () => Settings.EnableMeditate, v => Settings.EnableMeditate = v);
            CfgEnableFoodTolerance = CreateConfigEntry(nameof(Settings.EnableFoodTolerance), () => Settings.EnableFoodTolerance, v => Settings.EnableFoodTolerance = v);
            CfgEnableBegging = CreateConfigEntry(nameof(Settings.EnableBegging), () => Settings.EnableBegging, v => Settings.EnableBegging = v);
            CfgEnableCombatSkills = CreateConfigEntry(nameof(Settings.EnableCombatSkills), () => Settings.EnableCombatSkills, v => Settings.EnableCombatSkills = v);
            CfgCraftFromBags = CreateConfigEntry(nameof(Settings.CraftFromBags), () => Settings.CraftFromBags, v => Settings.CraftFromBags = v);
            CfgCraftFromBank = CreateConfigEntry(nameof(Settings.CraftFromBank), () => Settings.CraftFromBank, v => Settings.CraftFromBank = v);
            CfgEnableMagicSkills = CreateConfigEntry(nameof(Settings.EnableMagicSkills), () => Settings.EnableMagicSkills, v => Settings.EnableMagicSkills = v);
            CfgEnableSimPlayerSkills = CreateConfigEntry(nameof(Settings.EnableSimPlayerSkills), () => Settings.EnableSimPlayerSkills, v => Settings.EnableSimPlayerSkills = v);
            CfgEnableSimCraft = CreateConfigEntry(nameof(Settings.EnableSimCraft), () => Settings.EnableSimCraft, v => Settings.EnableSimCraft = v);
            CfgSimCraftFailCooldown = CreateConfigEntry(nameof(Settings.SimCraftFailCooldownSeconds), () => Settings.SimCraftFailCooldownSeconds, v => Settings.SimCraftFailCooldownSeconds = v);
            CfgSimChatIntervalMin = CreateConfigEntry(nameof(Settings.SimChatIntervalMinSeconds), () => Settings.SimChatIntervalMinSeconds, v => Settings.SimChatIntervalMinSeconds = v);
            CfgSimChatIntervalMax = CreateConfigEntry(nameof(Settings.SimChatIntervalMaxSeconds), () => Settings.SimChatIntervalMaxSeconds, v => Settings.SimChatIntervalMaxSeconds = v);
            CfgEnableSimWTB = CreateConfigEntry(nameof(Settings.EnableSimWTB), () => Settings.EnableSimWTB, v => Settings.EnableSimWTB = v);
            CfgEnableSimWTS = CreateConfigEntry(nameof(Settings.EnableSimWTS), () => Settings.EnableSimWTS, v => Settings.EnableSimWTS = v);
            CfgSimWTBExpiry = CreateConfigEntry(nameof(Settings.SimWTBExpirySeconds), () => Settings.SimWTBExpirySeconds, v => Settings.SimWTBExpirySeconds = v);
            CfgCatchUpCombatMagic = CreateConfigEntry(nameof(Settings.CatchUpCombatMagicSkills), () => Settings.CatchUpCombatMagicSkills, v => Settings.CatchUpCombatMagicSkills = v);
            CfgCatchUpUtilityTrade = CreateConfigEntry(nameof(Settings.CatchUpUtilityTradeskills), () => Settings.CatchUpUtilityTradeskills, v => Settings.CatchUpUtilityTradeskills = v);
            CfgForceCatchUp = CreateConfigEntry(nameof(Settings.ForceCatchUpOnNextLoad), () => Settings.ForceCatchUpOnNextLoad, v => Settings.ForceCatchUpOnNextLoad = v);

            CfgForageKey = CreateConfigEntry(nameof(Settings.ForageKey), () => Settings.ForageKey, v => Settings.ForageKey = v);
            CfgBindWoundKey = CreateConfigEntry(nameof(Settings.BindWoundKey), () => Settings.BindWoundKey, v => Settings.BindWoundKey = v);
            CfgBegKey = CreateConfigEntry(nameof(Settings.BegKey), () => Settings.BegKey, v => Settings.BegKey = v);
            CfgMeditateKey = CreateConfigEntry(nameof(Settings.MeditateKey), () => Settings.MeditateKey, v => Settings.MeditateKey = v);
            CfgBindWoundBaseHealPercent = CreateConfigEntry(nameof(Settings.BindWoundBaseHealPercent), () => Settings.BindWoundBaseHealPercent, v => Settings.BindWoundBaseHealPercent = v);
            CfgMeditateBaseMana = CreateConfigEntry(nameof(Settings.MeditateBaseMana), () => Settings.MeditateBaseMana, v => Settings.MeditateBaseMana = v);
            CfgCombatSkillDamagePerLevel = CreateConfigEntry(nameof(Settings.CombatSkillDamagePerLevel), () => Settings.CombatSkillDamagePerLevel, v => Settings.CombatSkillDamagePerLevel = v);
            CfgEvocationDamagePerLevel = CreateConfigEntry(nameof(Settings.EvocationDamagePerLevel), () => Settings.EvocationDamagePerLevel, v => Settings.EvocationDamagePerLevel = v);
            CfgAbjurationDurationPerLevel = CreateConfigEntry(nameof(Settings.AbjurationDurationPerLevel), () => Settings.AbjurationDurationPerLevel, v => Settings.AbjurationDurationPerLevel = v);
            CfgAlterationHealPerLevel = CreateConfigEntry(nameof(Settings.AlterationHealPerLevel), () => Settings.AlterationHealPerLevel, v => Settings.AlterationHealPerLevel = v);
            CfgConjurationDamagePerLevel = CreateConfigEntry(nameof(Settings.ConjurationDamagePerLevel), () => Settings.ConjurationDamagePerLevel, v => Settings.ConjurationDamagePerLevel = v);
            CfgTestCooldowns = CreateConfigEntry(nameof(Settings.TestCooldowns), () => Settings.TestCooldowns, v => Settings.TestCooldowns = v);
            CfgEnableTestMode = CreateConfigEntry(nameof(Settings.EnableTestMode), () => Settings.EnableTestMode, v => Settings.EnableTestMode = v);
            CfgEnableDebugCommands = CreateConfigEntry(nameof(Settings.EnableDebugCommands), () => Settings.EnableDebugCommands, v => Settings.EnableDebugCommands = v);

            Patches.AttackSyncConfig.Init(Settings);
            Features.LogDisplayOptions.BindConfig(Settings);
            Config.Save();
        }

        internal static ConfigEntry<T> CreateConfigEntry<T>(string key, Func<T> getter, Action<T> setter)
        {
            return new ConfigEntry<T>(key, getter, value =>
            {
                setter(value);
                SaveNativeConfig();
            });
        }

        internal static void SaveNativeConfig()
        {
            try { Instance?.Config?.Save(); }
            catch (Exception ex) { Log?.LogWarning("Could not save Lunaris settings: " + ex.Message); }
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
            // Lunaris unloads plugins in a collectible context. Stop every
            // activity and detach every external callback before clearing the
            // static state that would otherwise keep this assembly reachable.
            try { StopAllCoroutines(); } catch { }
            try { Patches.EverShorCharacterCreationPager.Shutdown(); } catch { }
            Patches.Patch_StatsTickEffectsCrashGuard.Reset();
            try { AddonInputState.Reset(); } catch { }
            try { Skills.SimPlayerTradeskills.OnSceneChange(); } catch { }

            // Persist game data and native Lunaris options before releasing refs.
            try
            {
                Items.ItemFactory.SaveCustomInventory();
                SkillsSaveManager.Save();
                Necromancer.NecromancerPetEquipment.Save();
            }
            catch (Exception ex) { Log?.LogWarning("Could not save character data during unload: " + ex.Message); }
            try { Config?.Save(); }
            catch (Exception ex) { Log?.LogWarning("Could not save Lunaris settings during unload: " + ex.Message); }

            try { UI.TradeskillWindow.Shutdown(); } catch { }
            try { UI.SimCraftTradeUI.Shutdown(); } catch { }
            try { UI.LogDisplaySettingsUI.Shutdown(); } catch { }
            try { UI.NativeUiStyle.Reset(); } catch { }
            try { Features.CorpseRunSystem.Shutdown(); } catch { }
            try { Necromancer.NecromancerCatalog.ResetRuntimeReferences(); } catch { }
            try { Monk.MonkCatalog.ResetRuntimeReferences(); } catch { }
            try { Items.BagWindow.Shutdown(); } catch { }
            try { Stations.CraftingStations.Shutdown(); } catch { }

            try { UnityEngine.SceneManagement.SceneManager.sceneLoaded -= OnSceneLoaded; } catch { }
            try { _harmony?.UnpatchSelf(); } catch (Exception ex) { Log?.LogWarning("Could not fully remove Harmony patches during unload: " + ex.Message); }
            _harmony = null;

            try { Patches.AttackSyncConfig.Reset(); } catch { }
            try { Features.LogDisplayOptions.Reset(); } catch { }
            ResetConfigEntryReferences();
            _settingsHandle = null;
            Settings = null;

            RuntimePaths.Reset();
            if (ReferenceEquals(Instance, this))
            {
                Instance = null;
                Log = null;
            }
        }

        private static void ResetConfigEntryReferences()
        {
            // The old feature code reads these small adapters. Clear all of
            // them in one place so no stale delegate retains the settings model.
            const System.Reflection.BindingFlags flags =
                System.Reflection.BindingFlags.Static |
                System.Reflection.BindingFlags.NonPublic;
            foreach (System.Reflection.FieldInfo field in typeof(SkillsPlugin).GetFields(flags))
            {
                Type fieldType = field.FieldType;
                if (fieldType.IsGenericType &&
                    fieldType.GetGenericTypeDefinition() == typeof(ConfigEntry<>))
                    field.SetValue(null, null);
            }
        }

        private void OnSceneLoaded(UnityEngine.SceneManagement.Scene scene, UnityEngine.SceneManagement.LoadSceneMode mode)
        {
            Patches.Patch_StatsTickEffectsCrashGuard.Reset();
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
