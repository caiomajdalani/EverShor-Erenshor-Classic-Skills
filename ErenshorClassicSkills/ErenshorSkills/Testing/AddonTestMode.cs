using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using BepInEx;
using HarmonyLib;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace ErenshorSkills.Testing
{
    public sealed class GearCaptureCameraHook : MonoBehaviour
    {
        internal Transform Target;
        internal bool ForceFront;
        internal bool ForcePose;
        internal Vector3 PosePosition;
        internal Quaternion PoseRotation;
        private bool _adjusted;
        private Vector3 _basePosition;
        private Quaternion _baseRotation;

        private void OnPreCull()
        {
            if (!ForcePose && (!ForceFront || Target == null)) return;
            _basePosition = transform.position;
            _baseRotation = transform.rotation;
            if (ForcePose)
            {
                transform.position = PosePosition;
                transform.rotation = PoseRotation;
                _adjusted = true;
                return;
            }
            Vector3 pivot = Target.position + Vector3.up;
            Vector3 relative = _basePosition - pivot;
            Vector3 opposite = pivot +
                new Vector3(-relative.x, relative.y, -relative.z);
            Vector3 lookDirection = pivot - opposite;
            if (lookDirection.sqrMagnitude < 0.01f) return;
            transform.position = opposite;
            transform.rotation = Quaternion.LookRotation(lookDirection,
                Vector3.up);
            _adjusted = true;
        }

        private void OnPostRender()
        {
            if (!_adjusted) return;
            transform.position = _basePosition;
            transform.rotation = _baseRotation;
            _adjusted = false;
        }
    }

    /// <summary>
    /// Opt-in, in-game verification harness. Temporary smoke probes are restored
    /// in memory before the next frame and are never saved. Explicit item grants
    /// use the same crash-recoverable transaction as crafting and bag movement.
    /// </summary>
    public sealed class AddonTestMode : MonoBehaviour
    {
        private const int ExpectedRecipeCount = 280;
        private const int SearchResultLimit = 20;
        private const int ItemsPerFrame = 8;
        private const string CapacityFillerItemName = "Padded Cloth Cap";
        private const string QaCommandFileName = "qa-command.txt";
        private const int MaximumQaCommandLength = 1024;
        internal const string NecromancerQaCharacterName = "John EverQuest";
        private const string NecromancerQaCharacterAlias = "John Necromancer";
        private static readonly string[] TradeskillNames =
        {
            "Smithing", "Baking", "Brewing",
            "Fletching", "Jewelcraft", "Tailoring"
        };

        private static AddonTestMode _instance;
        private bool _smokeRunning;
        private bool _gearVisualRunning;
        private bool _necromancerVfxRunning;
        private bool _necromancerCastRunning;
        private bool _necromancerPetRoleRunning;
        private bool _necromancerPetMotionRunning;
        private bool _necromancerVendorCaptureRunning;
        private bool _necromancerIconCaptureRunning;
        private bool _craftedIconCaptureRunning;
        private bool _necromancerAuraTestRunning;
        private bool _necromancerGearTestRunning;
        private bool _logDisplayCaptureRunning;
        private bool _qaBootstrapRunning;
        private string _gearVisualLabel;
        private string _gearFilter;
        private string _lastReportPath;
        private float _nextQaCommandPoll;

        private static readonly MethodInfo EquipmentEligibilityMethod =
            typeof(ItemIcon).GetMethod("DoInitialChecks",
                BindingFlags.Instance | BindingFlags.NonPublic, null,
                new[] { typeof(Item), typeof(ItemIcon) }, null);

        private sealed class CatalogEntry
        {
            public string Name;
            public Item Item;
            public string Source;
        }

        private sealed class EquipmentSnapshot
        {
            public readonly List<Item> Items = new List<Item>();
            public readonly List<int> Quantities = new List<int>();
            public Item Aura;
            public int AuraQuantity;
            public Item Charm;
            public int CharmQuantity;
            public bool CharacterCameraActive;
            public int HidePlayerHelm;
            public Quaternion PlayerModelRotation;
            public int CurrentHp;
            public int CurrentMana;
        }

        private sealed class VisualCaptureState
        {
            public Character Player;
            public int PlayerHp;
            public bool PlayerInvulnerable;
            public readonly List<CanvasState> Canvases =
                new List<CanvasState>();
            public readonly List<RendererState> BystanderRenderers =
                new List<RendererState>();
            public readonly List<BehaviourState> BystanderBehaviours =
                new List<BehaviourState>();
        }

        private sealed class CanvasState
        {
            public Canvas Canvas;
            public bool Enabled;
        }

        private sealed class RendererState
        {
            public Renderer Renderer;
            public bool Enabled;
        }

        private sealed class BehaviourState
        {
            public Behaviour Behaviour;
            public bool Enabled;
        }

        private sealed class SmokeReport
        {
            private readonly StringBuilder _lines = new StringBuilder();
            public int Passed { get; private set; }
            public int Failed { get; private set; }

            public SmokeReport()
            {
                _lines.AppendLine("Erenshor Classic Skills - In-Game Smoke Report");
                _lines.AppendLine($"UTC: {DateTime.UtcNow:O}");
                _lines.AppendLine($"Addon: {SkillsPlugin.PluginVersion}");
                _lines.AppendLine($"Unity: {Application.unityVersion}");
                _lines.AppendLine($"Game application version: {Application.version}");
                _lines.AppendLine($"Scene: {SceneManager.GetActiveScene().name}");
                _lines.AppendLine($"Character: {SkillsSaveManager.CharName}");
                _lines.AppendLine();
            }

            public void Check(string scope, string name, bool passed,
                string detail = null)
            {
                if (passed) Passed++; else Failed++;
                _lines.Append(passed ? "[PASS] " : "[FAIL] ");
                _lines.Append(scope).Append(" | ").Append(name);
                if (!string.IsNullOrWhiteSpace(detail))
                    _lines.Append(" | ").Append(Clean(detail));
                _lines.AppendLine();
            }

            public string Finish(int itemCount, int recipeCount)
            {
                _lines.AppendLine();
                _lines.AppendLine(
                    $"SUMMARY | pass={Passed} fail={Failed} " +
                    $"items={itemCount} recipes={recipeCount}");
                return _lines.ToString();
            }

            private static string Clean(string value)
            {
                return (value ?? "").Replace('\r', ' ').Replace('\n', ' ');
            }
        }

        private void Awake()
        {
            _instance = this;
        }

        private void OnDestroy()
        {
            if (ReferenceEquals(_instance, this)) _instance = null;
        }

        private void Update()
        {
            if (SkillsPlugin.CfgEnableTestMode?.Value != true) return;

            PollQaCommandFile();
            if (!Input.GetKeyDown(KeyCode.F8)) return;

            MainMenu mainMenu = UnityEngine.Object.FindObjectOfType<MainMenu>();
            if (mainMenu != null)
            {
                if (mainMenu.FirstTime != null && mainMenu.FirstTime.activeSelf)
                {
                    mainMenu.ContinueClassic();
                    SkillsPlugin.Log.LogInfo(
                        "QA bootstrap: dismissed the first-time notice; press F8 again to log in.");
                }
                else
                {
                    mainMenu.LaunchGame();
                    SkillsPlugin.Log.LogInfo(
                        "QA bootstrap: started the character-select transition.");
                }
                return;
            }

            if (!GameData.InCharSelect) return;
            HandleQaCharacterHotkey();
        }

        private void LateUpdate()
        {
            if (SkillsPlugin.CfgEnableTestMode?.Value == true)
                Level35DpsBenchmark.EnforceRequestedTimeScale();
        }

        private void PollQaCommandFile()
        {
            if (Time.unscaledTime < _nextQaCommandPoll) return;
            _nextQaCommandPoll = Time.unscaledTime + 0.5f;

            string path = Path.Combine(RuntimePaths.DataDirectory,
                QaCommandFileName);
            if (!File.Exists(path)) return;
            try
            {
                string command = File.ReadAllText(path).Trim();
                File.Delete(path);
                if (command.Length == 0) return;
                if (command.Length > MaximumQaCommandLength ||
                    !command.StartsWith("/cstest",
                        StringComparison.OrdinalIgnoreCase))
                {
                    SkillsPlugin.Log.LogWarning(
                        $"Ignored invalid QA command inbox entry ({command.Length} chars)." );
                    return;
                }

                SkillsPlugin.Log.LogInfo(
                    "Executing test-mode QA command inbox entry.");
                HandleChatCommand(command);
            }
            catch (Exception ex)
            {
                SkillsPlugin.Log.LogError(
                    $"QA command inbox execution failed: {ex}");
            }
        }

        private static void HandleQaCharacterHotkey()
        {
            CharSelectManager manager = UnityEngine.Object
                .FindObjectOfType<CharSelectManager>();
            if (manager == null || GameData.SaveSlots == null ||
                GameData.SaveSlots.Count <= 2)
            {
                SkillsPlugin.Log.LogError(
                    "QA bootstrap: character-select data is not ready.");
                return;
            }

            SaveGameData slot = GameData.SaveSlots[2];
            if (!string.IsNullOrEmpty(slot.CharName) &&
                !string.Equals(slot.CharName, NecromancerQaCharacterName,
                    StringComparison.Ordinal))
            {
                SkillsPlugin.Log.LogError(
                    $"QA bootstrap refused occupied slot 2 '{slot.CharName}'.");
                return;
            }

            if (!ReferenceEquals(GameData.CurrentCharacterSlot, slot))
                manager.SelectSlot(2);

            if (string.IsNullOrEmpty(slot.CharName))
            {
                bool prepared = string.Equals(manager.Name?.text,
                        NecromancerQaCharacterName, StringComparison.Ordinal) &&
                    Necromancer.NecromancerCatalog.IsNecromancerClass(
                        manager.selClass);
                if (!prepared)
                {
                    if (manager.Name != null)
                        manager.Name.text = NecromancerQaCharacterName;
                    Necromancer.NecromancerCharacterCreation
                        .SelectNecromancer(manager);
                    SkillsPlugin.Log.LogInfo(
                        "Necromancer QA bootstrap: prepared John EverQuest in empty slot 2; press F8 again to save.");
                    return;
                }

                manager.SaveChar();
                SkillsPlugin.Log.LogInfo(
                    "Necromancer QA bootstrap: saved John EverQuest; press F8 again to enter Stowaway.");
                return;
            }

            bool necromancer = string.Equals(slot.CharClass,
                Necromancer.NecromancerCatalog.ClassName,
                StringComparison.OrdinalIgnoreCase);
            bool monk = string.Equals(slot.CharClass,
                Monk.MonkCatalog.ClassName,
                StringComparison.OrdinalIgnoreCase);
            if (!necromancer && !monk)
            {
                SkillsPlugin.Log.LogError(
                    $"QA bootstrap refused slot 2 class '{slot.CharClass}'.");
                return;
            }

            if (monk)
                Monk.MonkCharacterCreation.RestoreSelectedClass(manager);
            else
                Necromancer.NecromancerCharacterCreation
                    .RestoreSelectedClass(manager);
            SkillsPlugin.Log.LogInfo(
                $"QA bootstrap: entering the world as John EverQuest " +
                $"({slot.CharClass}).");
            manager.Play();
        }

        private void OnGUI()
        {
            if ((!_gearVisualRunning && !_necromancerVfxRunning &&
                !_necromancerCastRunning) ||
                string.IsNullOrEmpty(_gearVisualLabel)) return;
            var style = new GUIStyle(GUI.skin.box)
            {
                alignment = TextAnchor.MiddleLeft,
                fontSize = 22,
                fontStyle = FontStyle.Bold,
                wordWrap = true
            };
            style.normal.textColor = Color.white;
            GUI.depth = -10000;
            GUI.Box(new Rect(18f, 18f, Mathf.Min(1100f, Screen.width - 36f), 82f),
                _gearVisualLabel, style);
        }

        internal static void HandleChatCommand(string raw)
        {
            SkillsPlugin.Log.LogInfo($"[Test Command] {(raw ?? "").Trim()}");
            if (SkillsPlugin.CfgEnableTestMode?.Value != true)
            {
                ChatHelper.Send(
                    "<color=#EF5350>[Test Mode]</color> Disabled. Set " +
                    "Testing.EnableTestMode=true in the Classic Skills config " +
                    "and restart Erenshor for controlled testing.");
                return;
            }
            if (_instance == null)
            {
                ChatHelper.Send(
                    "<color=#EF5350>[Test Mode]</color> Runtime component is not ready.");
                return;
            }
            _instance.Dispatch(raw ?? "");
        }

        private void Dispatch(string raw)
        {
            string command = raw.Trim();
            string remainder;
            if (command.StartsWith("/csitem", StringComparison.OrdinalIgnoreCase))
                remainder = "item " + command.Substring(7).Trim();
            else
                remainder = command.Length > 7 ? command.Substring(7).Trim() : "help";

            string verb = TakeFirstToken(ref remainder).ToLowerInvariant();
            switch (verb)
            {
                case "":
                case "help": ShowHelp(); break;
                case "find": FindItems(remainder); break;
                case "item": GrantItem(remainder); break;
                case "quality": SetInventoryItemQuality(remainder); break;
                case "materials":
                case "mats": GrantRecipeMaterials(remainder); break;
                case "skill": SetSkill(remainder); break;
                case "skillprogress": SetSkillProgress(remainder); break;
                case "cooldowns": SetCooldowns(remainder); break;
                case "open": OpenWindow(remainder); break;
                case "tradeui": OpenTradeskillQaPreview(remainder); break;
                case "vendor": OpenRecipeVendorBoundary(remainder); break;
                case "smoke":
                case "all": StartSmoke(); break;
                case "gear": StartGearSmoke(remainder); break;
                case "corpse": TestCorpseRun(remainder); break;
                case "capacity": TestInventoryCapacity(remainder); break;
                case "pet": TestPetDamage(remainder); break;
                case "logs":
                case "logdisplay": StartLogDisplaySmoke(); break;
                case "icons": RunGlobalIconAudit(); break;
                case "craftedicons": StartCraftedIconSmoke(); break;
                case "classes":
                case "consumables": TestNativeConsumableClasses(); break;
                case "classidentity": TestCustomClassIdentity(); break;
                case "bagtransfers": TestBagTransfers(); break;
                case "dumpdb": DatabaseDumper.DumpAll(); break;
                case "simitems": TestSimPlayerCraftedItems(); break;
                case "simclasses": TestCustomClassSimPlayers(); break;
                case "enterqa": StartQaWorldBootstrap(); break;
                case "necromancer":
                case "necro": TestNecromancer(remainder); break;
                case "monk": Monk.MonkQaHarness.Handle(remainder); break;
                case "status": ShowStatus(); break;
                case "hudstate": StartCoroutine(ProbeHudState()); break;
                case "uidump": DumpNativeUiHierarchy(); break;
                default:
                    ChatHelper.Send(
                        $"<color=#EF5350>[Test Mode]</color> Unknown command '{verb}'. " +
                        "Use /cstest help.");
                    break;
            }
        }

        private static void TestCustomClassIdentity()
        {
            bool passed = Patches.CustomClassIdentity.RunSelfTest(
                out string detail);
            string result = passed ? "PASS" : "FAIL";
            SkillsPlugin.Log.LogInfo(
                $"Custom class identity QA {result}: {detail}");
            ChatHelper.Send(
                $"<color={(passed ? "#4CAF50" : "#EF5350")}>" +
                $"[Test Mode] Custom class identity {result}: {detail}</color>");
        }

        private static void TestCustomClassSimPlayers()
        {
            bool passed = Features.CustomClassSimPlayers.RunSelfTest(
                out string detail);
            string result = passed ? "PASS" : "FAIL";
            SkillsPlugin.Log.LogInfo(
                $"Custom class SimPlayer QA {result}: {detail}");
            ChatHelper.Send(
                $"<color={(passed ? "#4CAF50" : "#EF5350")}>" +
                $"[Test Mode] Custom class SimPlayers {result}: " +
                $"{detail}</color>");
        }

        private static void ShowHelp()
        {
            ChatHelper.Send("<color=#FFD54F>Classic Skills Test Mode</color>");
            ChatHelper.Send("/cstest find &lt;text&gt; - search addon and native items");
            ChatHelper.Send("/cstest item &lt;name&gt; [qty] - transactionally grant an item");
            ChatHelper.Send("/cstest quality &lt;slot&gt; &lt;value&gt; - set raw non-stackable gear quality");
            ChatHelper.Send("/cstest mats &lt;recipe&gt; - grant one combine's materials");
            ChatHelper.Send("/cstest skill &lt;name|all&gt; &lt;0-180&gt; - set test skill level");
            ChatHelper.Send("/cstest skillprogress &lt;name&gt; &lt;0-100&gt; - set next-level bar percent");
            ChatHelper.Send("/cstest cooldowns &lt;on|off&gt; - toggle short utility cooldowns");
            ChatHelper.Send("/cstest open &lt;skills|tradeskill&gt; - open a test window");
            ChatHelper.Send("/cstest classidentity - verify native load/save identity guards for Necromancer and Monk");
            ChatHelper.Send("/cstest tradeui &lt;tradeskill&gt; &lt;top|bottom&gt; - preview selection and scrolling without changing recipe knowledge");
            ChatHelper.Send("/cstest vendor &lt;stock|recipes&gt; - visually verify normal merchant stock precedes recipe pages");
            ChatHelper.Send("/cstest smoke - test every item, recipe, feature, and gear visual");
            ChatHelper.Send("/cstest corpse &lt;die|redie|status&gt; - exercise corpse-run death safeguards");
            ChatHelper.Send("/cstest capacity &lt;fill|clear|status&gt; - test full-inventory recovery safely");
            ChatHelper.Send("/cstest gear [filter] - rerun live equip and visual evidence");
            ChatHelper.Send("/cstest corpse &lt;die|status&gt; - exercise the real corpse-run death path");
            ChatHelper.Send("/cstest pet &lt;summon|probe|attack|status&gt; - verify Conjuration pet damage");
            ChatHelper.Send("/cstest logs - visually test Extra Settings, native menu buttons, every configured log color, and resolved damage types");
            ChatHelper.Send("/cstest icons - audit every custom item, Monk/Necromancer ability, aura, gear, scroll, and class icon at runtime");
            ChatHelper.Send("/cstest craftedicons - capture every crafted item icon in-game");
            ChatHelper.Send("/cstest classes - verify every item tooltip lists Monk and Necromancer whenever its real class restrictions permit them");
            ChatHelper.Send("/cstest bagtransfers - audit filled-bag bank deposit/reload/withdrawal/rollback plus Sim, vendor, and trash conservation");
            ChatHelper.Send("/cstest dumpdb - export current databases and all prefab spell-acquisition sources");
            ChatHelper.Send("/cstest simitems - live-test crafted gear plus quantity-backed SimPlayer food and drink");
            ChatHelper.Send("/cstest simclasses - verify persistent Necromancer and Monk Sims, combat kits, roles, native fallbacks, and chatter");
            ChatHelper.Send("/cstest enterqa - enter the isolated John EverQuest QA character without mouse input");
            ChatHelper.Send("/cstest necro &lt;status|skills|class|learn|cast|castall|vfx|icons|aura [verify]|gear [verify]|ascension|petgear|petrole|epic|travel|vendor [capture]|dps [quick|full|progression|progression-sustained|progression-quick|level N [quick|balance|full]]&gt; - verify the Necromancer class, shared Arcanist combat skills, unique icons, auras, class gear, epic drops, native charm forge, vendors, durable pet role, and visuals as John EverQuest");
            ChatHelper.Send("/cstest monk &lt;status|class|level 1-35|learn|skills|icons|vfx [NAME|all]|animations|capture [NAME|strikes|all]|use NAME|mechanics [NAME|all]|dps [quick|timescale|audit]|gear [verify]|aura [verify]|ascension|vendor [open]|smoke&gt; - targeted QA for newly added Monk content only, including bespoke punch/kick VFX and comparative level/gear DPS audits");
            ChatHelper.Send("/cstest uidump - write native Erenshor UI hierarchy evidence");
            ChatHelper.Send("/cstest status - show runner and last-report status");
        }

        private void TestSimPlayerCraftedItems()
        {
            if (!RequireLoadedWorld()) return;
            bool catalog = Items.SimPlayerCraftedItems.RunCatalogSelfTest(
                out string catalogDetail);
            List<Item> offhandProbes = Items.ItemFactory
                .GetCraftedOffhandWeaponsForTesting();
            SimPlayer sim = UnityEngine.Object.FindObjectsOfType<SimPlayer>()
                .Where(candidate => candidate != null &&
                    candidate.VerifyLoadComplete && candidate.MyStats != null &&
                    candidate.MyEquipment != null &&
                    candidate.AllHeldItems != null)
                .OrderByDescending(candidate => offhandProbes.Any(item =>
                    item?.Classes?.Contains(
                        candidate.MyStats.CharacterClass) == true))
                .ThenBy(candidate =>
                    candidate.GetComponent<NPC>()?.NPCName ??
                    candidate.transform.name)
                .FirstOrDefault();
            bool live = Items.SimPlayerCraftedItems.RunLiveSelfTest(sim,
                out string liveDetail);
            ChatHelper.Send(
                $"<color={(catalog && live ? "#4CAF50" : "#EF5350")}>" +
                $"[Test Mode] SimPlayer crafted items " +
                $"{(catalog && live ? "PASS" : "FAIL")}.</color>");
            ChatHelper.Send("<color=#AAAAAA>Catalog: " + catalogDetail +
                "; Live: " + liveDetail + "</color>");
        }

        private void TestBagTransfers()
        {
            if (!RequireLoadedWorld()) return;
            bool correctQaPlayer = string.Equals(
                SkillsSaveManager.CharName, "John EverQuest",
                StringComparison.Ordinal);
            string detail;
            bool pass;
            if (correctQaPlayer)
                pass = Items.BagTransferSafety.RunConservationAudit(
                    out detail);
            else
            {
                pass = false;
                detail = "QA transaction audit requires the exact character " +
                    "'John EverQuest'; active=" + SkillsSaveManager.CharName;
            }
            string path = Path.Combine(RuntimePaths.DataDirectory,
                "TestReports", $"bag-transfers-{DateTime.UtcNow:yyyyMMdd-HHmmss}-" +
                $"{SafeReportName(SkillsSaveManager.CharName)}.txt");
            string report =
                "Classic Skills crafted-bag transfer audit\n" +
                $"QA player: {SkillsSaveManager.CharName}\n" +
                $"Result: {(pass ? "PASS" : "FAIL")}\n" +
                $"Detail: {detail}\n";
            if (!SafeFileIO.TryWriteAllTextAtomic(path, report,
                out string reportError))
            {
                pass = false;
                detail += "; report write failed: " + reportError;
            }
            ChatHelper.Send(
                $"<color={(pass ? "#4CAF50" : "#EF5350")}>" +
                $"[Test Mode] Bag transfer audit " +
                $"{(pass ? "PASS" : "FAIL")}.</color>");
            ChatHelper.Send("<color=#AAAAAA>" + detail + "</color>");
            SkillsPlugin.Log.LogInfo(
                $"Bag transfer audit {(pass ? "PASS" : "FAIL")}: " +
                $"{detail}; report={path}");
        }

        private void StartCraftedIconSmoke()
        {
            if (!RequireLoadedWorld()) return;
            if (!Items.ItemFactory.Initialized)
            {
                ChatHelper.Send(
                    "<color=#EF5350>[Test Mode]</color> Item database is not initialized yet.");
                return;
            }
            if (_craftedIconCaptureRunning || _logDisplayCaptureRunning ||
                _smokeRunning || _gearVisualRunning ||
                _necromancerVfxRunning || _necromancerCastRunning ||
                _necromancerVendorCaptureRunning ||
                _necromancerIconCaptureRunning)
            {
                ChatHelper.Send(
                    "<color=#FFD54F>[Test Mode]</color> Another visual or smoke capture is already active.");
                return;
            }
            StartCoroutine(CaptureCraftedIconCatalog());
        }

        private void RunGlobalIconAudit()
        {
            if (!RequireLoadedWorld()) return;
            if (!Items.ItemFactory.Initialized)
            {
                ChatHelper.Send(
                    "<color=#EF5350>[Test Mode]</color> Item database is not initialized yet.");
                return;
            }

            bool pass = AddonIconAudit.Validate(out string detail);
            string path = Path.Combine(RuntimePaths.DataDirectory,
                "TestReports", $"global-icon-audit-" +
                $"{DateTime.UtcNow:yyyyMMdd-HHmmss}-" +
                $"{SafeReportName(SkillsSaveManager.CharName)}.txt");
            string report =
                "Classic Skills global runtime icon audit\n" +
                $"QA player: {SkillsSaveManager.CharName}\n" +
                $"Result: {(pass ? "PASS" : "FAIL")}\n" +
                $"Detail: {detail}\n";
            if (!SafeFileIO.TryWriteAllTextAtomic(path, report,
                out string reportError))
            {
                pass = false;
                detail += "; report write failed: " + reportError;
            }

            ChatHelper.Send(
                $"<color={(pass ? "#4CAF50" : "#EF5350")}>" +
                $"[Test Mode] Global icon audit " +
                $"{(pass ? "PASS" : "FAIL")}.</color>");
            ChatHelper.Send("<color=#AAAAAA>" + detail + "</color>");
            SkillsPlugin.Log.LogInfo(
                $"Global icon audit {(pass ? "PASS" : "FAIL")}: " +
                $"{detail}; report={path}");
        }

        private void TestNativeConsumableClasses()
        {
            if (!RequireLoadedWorld()) return;
            Monk.MonkCatalog.TryInitialize();
            Necromancer.NecromancerCatalog.TryInitialize();
            bool pass =
                Items.NativeConsumableCompatibility.Validate(
                    out string detail);
            bool rendered =
                Items.NativeConsumableCompatibility.ValidateRenderedTooltips(
                    out string renderedDetail);
            pass &= rendered;
            detail += "; " + renderedDetail;
            string path = Path.Combine(RuntimePaths.DataDirectory,
                "TestReports", $"custom-class-tooltips-" +
                $"{DateTime.UtcNow:yyyyMMdd-HHmmss}-" +
                $"{SafeReportName(SkillsSaveManager.CharName)}.txt");
            string report =
                "Classic Skills custom-class item tooltip audit\n" +
                $"QA player: {SkillsSaveManager.CharName}\n" +
                $"Result: {(pass ? "PASS" : "FAIL")}\n" +
                $"Detail: {detail}\n";
            if (!SafeFileIO.TryWriteAllTextAtomic(path, report,
                out string reportError))
            {
                pass = false;
                detail += "; report write failed: " + reportError;
            }
            ChatHelper.Send(
                $"<color={(pass ? "#4CAF50" : "#EF5350")}>" +
                $"[Test Mode] Custom-class item tooltip audit " +
                $"{(pass ? "PASS" : "FAIL")}.</color>");
            ChatHelper.Send("<color=#AAAAAA>" + detail + "</color>");
            SkillsPlugin.Log.LogInfo(
                $"Custom-class item tooltip audit " +
                $"{(pass ? "PASS" : "FAIL")}: {detail}; report={path}");
        }

        private IEnumerator CaptureCraftedIconCatalog()
        {
            _craftedIconCaptureRunning = true;
            string directory = Path.Combine(RuntimePaths.DataDirectory,
                "TestReports", "CraftedItemIcons",
                $"{DateTime.UtcNow:yyyyMMdd-HHmmss}-" +
                SafeReportName(SkillsSaveManager.CharName));
            Directory.CreateDirectory(directory);
            var manifest = new StringBuilder();
            manifest.AppendLine("capture\tcount\tresult\tdetail");
            var items = new StringBuilder();
            items.AppendLine("tradeskill\tindex\titem\ttexture\tsize\tpage");
            GameObject overlay = null;
            int passed = 0;
            int failed = 0;
            int totalIcons = 0;
            try
            {
                GameData.PlayerInv?.ForceCloseInv();
                GameData.ItemInfoWindow?.CloseItemWindow();

                bool runtimeValid = Items.ItemFactory
                    .RunCraftedIconSelfTest(out string runtimeDetail);
                if (runtimeValid) passed++; else failed++;
                manifest.Append("runtime_icon_contract\t280\t")
                    .Append(runtimeValid ? "PASS" : "FAIL").Append('\t')
                    .AppendLine(Tsv(runtimeDetail));

                for (int skillIndex = 0;
                    skillIndex < TradeskillNames.Length; skillIndex++)
                {
                    string tradeskill = TradeskillNames[skillIndex];
                    List<Skills.Tradeskills.Recipe> recipes = Skills.Tradeskills
                        .GetRecipesForSkillPublic(tradeskill);
                    var entries = new List<KeyValuePair<string, Sprite>>();
                    var missing = new List<string>();
                    for (int index = 0; index < recipes.Count; index++)
                    {
                        Skills.Tradeskills.Recipe recipe = recipes[index];
                        Item item = recipe == null ? null :
                            Items.ItemFactory.GetItem(recipe.Name);
                        Sprite sprite = item?.ItemIcon;
                        if (recipe == null || item == null || sprite == null)
                        {
                            missing.Add(recipe?.Name ?? "<null>");
                            continue;
                        }
                        entries.Add(new KeyValuePair<string, Sprite>(
                            item.ItemName, sprite));
                        totalIcons++;
                        Texture2D texture = sprite.texture;
                        items.Append(Tsv(tradeskill)).Append('\t')
                            .Append(index + 1).Append('\t')
                            .Append(Tsv(item.ItemName)).Append('\t')
                            .Append(Tsv(texture?.name ?? "")).Append('\t')
                            .Append(texture == null ? "<none>" :
                                texture.width + "x" + texture.height)
                            .Append('\t')
                            .Append(skillIndex + 1).Append('_')
                            .Append(tradeskill.ToLowerInvariant())
                            .AppendLine(".png");
                    }

                    overlay = CreateIconAuditOverlay(
                        $"{tradeskill} Crafted Item Icons ({entries.Count})",
                        entries, 11);
                    yield return null;
                    yield return new WaitForEndOfFrame();
                    string fileName = $"{skillIndex + 1:D2}_" +
                        tradeskill.ToLowerInvariant() + ".png";
                    string path = Path.Combine(directory, fileName);
                    bool requested = RequestScreenshot(path,
                        out string screenshotError);
                    int wait = 0;
                    while (requested && !ScreenshotExists(path) &&
                        wait++ < 120) yield return null;
                    bool captured = requested && ScreenshotExists(path);
                    bool pageValid = missing.Count == 0 &&
                        entries.Count == recipes.Count && captured;
                    if (pageValid) passed++; else failed++;
                    string detail = missing.Count == 0
                        ? screenshotError ?? ""
                        : "missing=" + string.Join(",", missing);
                    manifest.Append(fileName).Append('\t')
                        .Append(entries.Count).Append('\t')
                        .Append(pageValid ? "PASS" : "FAIL").Append('\t')
                        .AppendLine(Tsv(detail));
                    if (overlay != null) Destroy(overlay);
                    overlay = null;
                    yield return null;
                }

                bool totalValid = totalIcons == ExpectedRecipeCount;
                if (totalValid) passed++; else failed++;
                manifest.Append("complete_catalog\t")
                    .Append(totalIcons).Append('\t')
                    .Append(totalValid ? "PASS" : "FAIL").Append('\t')
                    .AppendLine(totalValid ? "all recipe outputs captured" :
                        $"expected {ExpectedRecipeCount}");
                SafeFileIO.TryWriteAllTextAtomic(Path.Combine(directory,
                    "manifest.tsv"), manifest.ToString(), out _);
                SafeFileIO.TryWriteAllTextAtomic(Path.Combine(directory,
                    "items.tsv"), items.ToString(), out _);
                _lastReportPath = directory;
                ChatHelper.Send(
                    $"<color={(failed == 0 ? "#4CAF50" : "#EF5350")}>" +
                    $"[Test Mode] Crafted icon visual audit " +
                    $"{(failed == 0 ? "PASS" : "FAIL")}: " +
                    $"{totalIcons} icons, {passed} checks passed, " +
                    $"{failed} failed.</color>");
                ChatHelper.Send("<color=#AAAAAA>Evidence: " + directory +
                    "</color>");
            }
            finally
            {
                if (overlay != null) Destroy(overlay);
                _craftedIconCaptureRunning = false;
            }
        }

        private void StartQaWorldBootstrap()
        {
            if (_qaBootstrapRunning)
            {
                SkillsPlugin.Log.LogWarning(
                    "QA bootstrap is already running.");
                return;
            }
            StartCoroutine(EnterQaWorld());
        }

        private IEnumerator EnterQaWorld()
        {
            _qaBootstrapRunning = true;
            try
            {
                MainMenu mainMenu = UnityEngine.Object
                    .FindObjectOfType<MainMenu>();
                if (mainMenu != null)
                {
                    if (mainMenu.FirstTime != null &&
                        mainMenu.FirstTime.activeSelf)
                    {
                        mainMenu.ContinueClassic();
                        yield return null;
                    }
                    mainMenu.LaunchGame();
                    SkillsPlugin.Log.LogInfo(
                        "QA command bootstrap: started character-select transition.");
                }

                float deadline = Time.realtimeSinceStartup + 180f;
                while (Time.realtimeSinceStartup < deadline)
                {
                    if (SkillsSaveManager.HasLoaded &&
                        string.Equals(SkillsSaveManager.CharName,
                            NecromancerQaCharacterName,
                            StringComparison.Ordinal))
                    {
                        SkillsPlugin.Log.LogInfo(
                            "QA command bootstrap: John EverQuest entered the world.");
                        yield break;
                    }

                    if (GameData.InCharSelect)
                    {
                        CharSelectManager manager = UnityEngine.Object
                            .FindObjectOfType<CharSelectManager>();
                        bool simsReady = GameData.SimMngr == null ||
                            GameData.SimMngr.LoadedSimplayers;
                        if (manager != null && simsReady &&
                            GameData.SaveSlots != null &&
                            GameData.SaveSlots.Count > 2)
                        {
                            HandleQaCharacterHotkey();
                            // Existing QA characters enter immediately. A new
                            // empty slot advances prepare -> save -> enter on
                            // later guarded passes.
                            yield return new WaitForSecondsRealtime(0.5f);
                        }
                    }
                    yield return null;
                }
                SkillsPlugin.Log.LogError(
                    "QA command bootstrap timed out before John EverQuest entered the world.");
            }
            finally
            {
                _qaBootstrapRunning = false;
            }
        }

        private static void DumpNativeUiHierarchy()
        {
            if (!RequireLoadedWorld()) return;
            try
            {
                string path = Path.Combine(RuntimePaths.DataDirectory,
                    "native-ui-hierarchy.txt");
                var output = new StringBuilder(32768);
                output.AppendLine("Erenshor native UI hierarchy");
                output.AppendLine($"UTC: {DateTime.UtcNow:O}");
                output.AppendLine($"Unity: {Application.unityVersion}");
                output.AppendLine($"Game: {Application.version}");
                output.AppendLine($"Scene: {SceneManager.GetActiveScene().name}");
                output.AppendLine();

                DumpUiRoot(output, "Inventory",
                    GameData.PlayerInv?.InvWindow);
                DumpUiRoot(output, "Stats",
                    GameData.PlayerInv?.StatWindow);
                DumpUiRoot(output, "SkillBook",
                    GameData.PlayerSkillBook?.Skillbook);
                DumpUiRoot(output, "SpellBook",
                    GameData.PlayerSpellBook?.Spellbook);
                DumpUiRoot(output, "QuestLog",
                    GameData.QuestLog?.QuestWindow);
                DumpUiRoot(output, "Vendor",
                    GameData.VendorWindow?.WindowParent);
                DumpActiveHudButtons(output);

                Directory.CreateDirectory(RuntimePaths.DataDirectory);
                File.WriteAllText(path, output.ToString());
                SkillsPlugin.Log.LogInfo("Native UI hierarchy written: " + path);
                ChatHelper.Send(
                    "<color=#FFD54F>[Test Mode]</color> Native UI hierarchy written.");
                ChatHelper.Send($"<color=#AAAAAA>{path}</color>");
            }
            catch (Exception ex)
            {
                SkillsPlugin.Log.LogError("Native UI hierarchy dump failed: " + ex);
                ChatHelper.Send(
                    "<color=#EF5350>[Test Mode]</color> UI dump failed: " +
                    ex.Message);
            }
        }

        private static void DumpUiRoot(StringBuilder output, string label,
            GameObject root)
        {
            output.AppendLine($"=== {label} ===");
            if (!root)
            {
                output.AppendLine("<unavailable>");
                output.AppendLine();
                return;
            }
            DumpUiTransform(output, root.transform, root.transform, 0);
            output.AppendLine();
        }

        private static void DumpActiveHudButtons(StringBuilder output)
        {
            output.AppendLine("=== Active HUD Buttons ===");
            UnityEngine.UI.Button[] buttons =
                UnityEngine.Object.FindObjectsOfType<UnityEngine.UI.Button>();
            Array.Sort(buttons, (left, right) => string.Compare(
                GetAbsoluteTransformPath(left ? left.transform : null),
                GetAbsoluteTransformPath(right ? right.transform : null),
                StringComparison.OrdinalIgnoreCase));

            foreach (UnityEngine.UI.Button button in buttons)
            {
                if (!button || !button.gameObject.activeInHierarchy) continue;
                RectTransform rect = button.transform as RectTransform;
                if (!rect) continue;

                Vector3[] corners = new Vector3[4];
                rect.GetWorldCorners(corners);
                output.Append(GetAbsoluteTransformPath(button.transform))
                    .Append(" | rect=").Append(Vector(rect.rect.size))
                    .Append(" anchored=").Append(Vector(rect.anchoredPosition))
                    .Append(" world=").Append(Vector(corners[0]))
                    .Append("..").Append(Vector(corners[2]))
                    .Append(" transition=").Append(button.transition)
                    .Append(" interactable=").Append(button.interactable)
                    .Append(" target=")
                    .Append(button.targetGraphic ?
                        button.targetGraphic.name : "<none>");

                UnityEngine.UI.Image image =
                    button.targetGraphic as UnityEngine.UI.Image ??
                    button.GetComponent<UnityEngine.UI.Image>();
                if (image)
                {
                    output.Append(" | Image sprite=")
                        .Append(image.sprite ? image.sprite.name : "<none>")
                        .Append(" type=").Append(image.type)
                        .Append(" color=").Append(ColorValue(image.color))
                        .Append(" material=")
                        .Append(image.material ? image.material.name : "<none>")
                        .Append(" preserve=").Append(image.preserveAspect);
                }

                Canvas canvas = button.GetComponentInParent<Canvas>();
                if (canvas)
                    output.Append(" | canvas=").Append(canvas.name)
                        .Append('/').Append(canvas.renderMode)
                        .Append(" order=").Append(canvas.sortingOrder);
                output.AppendLine();
            }
            output.AppendLine();
        }

        private static string GetAbsoluteTransformPath(Transform current)
        {
            if (!current) return "<missing>";
            var parts = new List<string>();
            while (current)
            {
                parts.Add(current.name);
                current = current.parent;
            }
            parts.Reverse();
            return string.Join("/", parts.ToArray());
        }

        private static void DumpUiTransform(StringBuilder output,
            Transform root, Transform current, int depth)
        {
            string indent = new string(' ', depth * 2);
            string path = current == root ? current.name :
                GetRelativeTransformPath(root, current);
            output.Append(indent).Append(path)
                .Append(" | active=").Append(current.gameObject.activeSelf)
                .Append('/').Append(current.gameObject.activeInHierarchy);
            Component[] components = current.GetComponents<Component>();
            if (components.Length > 0)
            {
                output.Append(" | components=");
                for (int componentIndex = 0;
                    componentIndex < components.Length; componentIndex++)
                {
                    if (componentIndex > 0) output.Append(',');
                    Component component = components[componentIndex];
                    output.Append(component ? component.GetType().Name : "<missing>");
                }
            }

            RectTransform rect = current as RectTransform;
            if (rect)
            {
                output.Append(" | rect=").Append(Vector(rect.rect.size))
                    .Append(" pos=").Append(Vector(rect.anchoredPosition))
                    .Append(" anchor=").Append(Vector(rect.anchorMin))
                    .Append("..").Append(Vector(rect.anchorMax))
                    .Append(" pivot=").Append(Vector(rect.pivot))
                    .Append(" scale=").Append(Vector(rect.localScale));
            }

            var image = current.GetComponent<UnityEngine.UI.Image>();
            if (image)
            {
                output.Append(" | Image sprite=")
                    .Append(image.sprite ? image.sprite.name : "<none>")
                    .Append(" type=").Append(image.type)
                    .Append(" color=").Append(ColorValue(image.color))
                    .Append(" material=")
                    .Append(image.material ? image.material.name : "<none>")
                    .Append(" preserve=").Append(image.preserveAspect)
                    .Append(" raycast=").Append(image.raycastTarget);
                if (image.sprite)
                    output.Append(" border=").Append(Vector(image.sprite.border));
            }

            var raw = current.GetComponent<UnityEngine.UI.RawImage>();
            if (raw)
                output.Append(" | RawImage texture=")
                    .Append(raw.texture ? raw.texture.name : "<none>")
                    .Append(" color=").Append(ColorValue(raw.color));

            var text = current.GetComponent<TMPro.TextMeshProUGUI>();
            if (text)
                output.Append(" | TMP text=\"").Append(CleanInline(text.text))
                    .Append("\" font=").Append(text.font ? text.font.name : "<none>")
                    .Append(" material=")
                    .Append(text.fontSharedMaterial ?
                        text.fontSharedMaterial.name : "<none>")
                    .Append(" size=").Append(text.fontSize.ToString("0.##"))
                    .Append(" style=").Append(text.fontStyle)
                    .Append(" align=").Append(text.alignment)
                    .Append(" color=").Append(ColorValue(text.color));

            var button = current.GetComponent<UnityEngine.UI.Button>();
            if (button)
                output.Append(" | Button transition=").Append(button.transition)
                    .Append(" target=")
                    .Append(button.targetGraphic ? button.targetGraphic.name : "<none>")
                    .Append(" colors=").Append(ColorValue(button.colors.normalColor))
                    .Append('/').Append(ColorValue(button.colors.highlightedColor))
                    .Append('/').Append(ColorValue(button.colors.pressedColor));

            var scroll = current.GetComponent<UnityEngine.UI.ScrollRect>();
            if (scroll)
                output.Append(" | ScrollRect viewport=")
                    .Append(scroll.viewport ? scroll.viewport.name : "<none>")
                    .Append(" content=")
                    .Append(scroll.content ? scroll.content.name : "<none>")
                    .Append(" sensitivity=").Append(scroll.scrollSensitivity);

            var scrollbar = current.GetComponent<UnityEngine.UI.Scrollbar>();
            if (scrollbar)
                output.Append(" | Scrollbar direction=")
                    .Append(scrollbar.direction);

            output.AppendLine();
            for (int i = 0; i < current.childCount; i++)
                DumpUiTransform(output, root, current.GetChild(i), depth + 1);
        }

        private static string GetRelativeTransformPath(Transform root,
            Transform current)
        {
            var parts = new List<string>();
            while (current && current != root)
            {
                parts.Add(current.name);
                current = current.parent;
            }
            parts.Reverse();
            return string.Join("/", parts.ToArray());
        }

        private static string CleanInline(string value)
        {
            string cleaned = (value ?? "").Replace('\r', ' ')
                .Replace('\n', ' ').Replace('\t', ' ');
            return cleaned.Length <= 100 ? cleaned :
                cleaned.Substring(0, 100) + "...";
        }

        private static string Vector(Vector2 value)
        {
            return $"({value.x:0.##},{value.y:0.##})";
        }

        private static string Vector(Vector3 value)
        {
            return $"({value.x:0.##},{value.y:0.##},{value.z:0.##})";
        }

        private static string Vector(Vector4 value)
        {
            return $"({value.x:0.##},{value.y:0.##},{value.z:0.##},{value.w:0.##})";
        }

        private static string ColorValue(Color value)
        {
            return $"({value.r:0.###},{value.g:0.###}," +
                $"{value.b:0.###},{value.a:0.###})";
        }

        private IEnumerator ProbeHudState()
        {
            GameObject escapeMenu = GameData.GM?.EscapeMenu;
            UtilityActionBar actionBar =
                SkillsPlugin.Instance?.GetComponent<UtilityActionBar>();
            if (escapeMenu == null)
            {
                SkillsPlugin.Log.LogWarning(
                    "HUD state probe: GameData.GM.EscapeMenu is unavailable.");
                ChatHelper.Send(
                    "<color=#EF5350>[Test Mode]</color> Escape menu is unavailable.");
                yield break;
            }

            bool wasActive = escapeMenu.activeSelf;
            SkillsPlugin.Log.LogInfo(
                $"HUD state probe before: name={escapeMenu.name}; " +
                $"self={escapeMenu.activeSelf}; hierarchy={escapeMenu.activeInHierarchy}; " +
                $"detected={SkillsPlugin.IsEscapeMenuOpen}; hud={actionBar?.IsVisibleForTesting}");
            escapeMenu.SetActive(true);
            yield return null;
            yield return null;
            bool detected = SkillsPlugin.IsEscapeMenuOpen;
            bool hidden = actionBar?.IsVisibleForTesting == false;
            SkillsPlugin.Log.LogInfo(
                $"HUD state probe open: name={escapeMenu.name}; " +
                $"self={escapeMenu.activeSelf}; hierarchy={escapeMenu.activeInHierarchy}; " +
                $"detected={detected}; hud={actionBar?.IsVisibleForTesting}");
            escapeMenu.SetActive(wasActive);
            yield return null;
            ChatHelper.Send(
                $"<color=#FFD54F>[Test Mode]</color> Pause-menu HUD guard: " +
                $"{(detected && hidden ? "PASS" : "FAIL")}.");
        }

        private void ShowStatus()
        {
            int addonCount = Items.ItemFactory.Initialized
                ? BuildCatalog(false).Count : 0;
            ChatHelper.Send(
                $"<color=#FFD54F>[Test Mode]</color> " +
                $"smoke={(_smokeRunning ? "running" : "idle")}, " +
                $"gear visuals={(_gearVisualRunning ? "capturing" : "idle")}, " +
                $"crafted icons={(_craftedIconCaptureRunning ? "capturing" : "idle")}, " +
                $"addon catalog={addonCount}, test cooldowns=" +
                $"{(SkillsPlugin.CfgTestCooldowns?.Value == true ? "on" : "off")}.");
            ChatHelper.Send(
                $"<color=#AAAAAA>Corpse Run: " +
                $"{Features.CorpseRunSystem.GetStatusForTesting()}</color>");
            ChatHelper.Send(
                $"<color=#AAAAAA>Loader: {RuntimePaths.LoaderName}; " +
                $"data: {RuntimePaths.DataDirectory}</color>");
            ChatHelper.Send(
                $"<color=#AAAAAA>Necromancer: " +
                $"{(Necromancer.NecromancerCatalog.IsReady ? "ready" : "not ready")}; " +
                    $"VFX preview={(_necromancerVfxRunning ? "running" : "idle")}; " +
                    $"cast sweep={(_necromancerCastRunning ? "running" : "idle")}; " +
                    $"pet role={(_necromancerPetRoleRunning ? "running" : "idle")}; " +
                    $"icon capture={(_necromancerIconCaptureRunning ? "running" : "idle")}; " +
                    $"aura test={(_necromancerAuraTestRunning ? "running" : "idle")}; " +
                    $"class gear test={(_necromancerGearTestRunning ? "running" : "idle")}; " +
                    $"vendor capture={(_necromancerVendorCaptureRunning ? "running" : "idle")}; " +
                    $"pet gear={Necromancer.NecromancerPetEquipment.Describe()}</color>");
            ChatHelper.Send(
                $"<color=#AAAAAA>Log display: custom colors=" +
                $"{(Features.LogDisplayOptions.EnableCustomColors?.Value == true ? "on" : "off")}; " +
                $"damage types={(Features.LogDisplayOptions.ShowDamageTypes?.Value == true ? "on" : "off")}; " +
                $"visual capture={(_logDisplayCaptureRunning ? "running" : "idle")}</color>");
            if (!string.IsNullOrEmpty(_lastReportPath))
                ChatHelper.Send($"<color=#AAAAAA>Last report: {_lastReportPath}</color>");
        }

        private void StartLogDisplaySmoke()
        {
            if (!RequireLoadedWorld()) return;
            if (_logDisplayCaptureRunning || _smokeRunning ||
                _craftedIconCaptureRunning ||
                _gearVisualRunning || _necromancerVfxRunning ||
                _necromancerCastRunning || _necromancerVendorCaptureRunning)
            {
                ChatHelper.Send(
                    "<color=#FFD54F>[Test Mode]</color> Another visual or smoke capture is already active.");
                return;
            }
            if (Features.LogDisplayOptions.EnableCustomColors?.Value != true ||
                Features.LogDisplayOptions.ShowDamageTypes?.Value != true)
            {
                ChatHelper.Send(
                    "<color=#EF5350>[Test Mode]</color> Enable Chat and Combat Log.EnableCustomLogColors and ShowDamageTypes before the visual smoke test.");
                return;
            }
            StartCoroutine(CaptureLogDisplaySmoke());
        }

        private IEnumerator CaptureLogDisplaySmoke()
        {
            _logDisplayCaptureRunning = true;
            string directory = Path.Combine(RuntimePaths.DataDirectory,
                "TestReports", "LogDisplay",
                $"{DateTime.UtcNow:yyyyMMdd-HHmmss}-" +
                SafeReportName(SkillsSaveManager.CharName));
            Directory.CreateDirectory(directory);
            var manifest = new StringBuilder();
            manifest.AppendLine("capture\tresult\tdetail");
            bool selfTest = Features.LogDisplayOptions.RunSelfTest(
                out string selfTestDetail);
            bool settingsUiSelfTest = false;
            string settingsUiSelfTestDetail = "not run";
            int passed = 0;
            int failed = 0;
            try
            {
                GameData.PlayerInv?.ForceCloseInv();
                GameData.ItemInfoWindow?.CloseItemWindow();

                bool settingsTopOpened =
                    UI.LogDisplaySettingsUI.OpenForTesting(0, false,
                        out string settingsTopDetail);
                yield return new WaitForSecondsRealtime(0.25f);
                yield return new WaitForEndOfFrame();
                string settingsTopName = "extra_settings_top.png";
                string settingsTopPath = Path.Combine(directory,
                    settingsTopName);
                string settingsTopError = null;
                bool settingsTopRequested = settingsTopOpened &&
                    RequestScreenshot(settingsTopPath,
                        out settingsTopError);
                int settingsTopWait = 0;
                while (settingsTopRequested &&
                    !ScreenshotExists(settingsTopPath) &&
                    settingsTopWait++ < 120) yield return null;
                bool settingsTopExists = settingsTopRequested &&
                    ScreenshotExists(settingsTopPath);
                if (settingsTopExists) passed++; else failed++;
                manifest.Append(settingsTopName).Append('\t')
                    .Append(settingsTopExists ? "PASS" : "FAIL").Append('\t')
                    .AppendLine(Tsv(settingsTopDetail + "; " +
                        (settingsTopError ?? "")));

                settingsUiSelfTest =
                    UI.LogDisplaySettingsUI.RunSelfTest(
                        out settingsUiSelfTestDetail);

                bool settingsBottomOpened =
                    UI.LogDisplaySettingsUI.OpenForTesting(
                        Features.LogDisplayOptions.ColorCategoryCount - 1,
                        true, out string settingsBottomDetail);
                yield return new WaitForSecondsRealtime(0.25f);
                yield return new WaitForEndOfFrame();
                string settingsBottomName = "extra_settings_bottom.png";
                string settingsBottomPath = Path.Combine(directory,
                    settingsBottomName);
                string settingsBottomError = null;
                bool settingsBottomRequested = settingsBottomOpened &&
                    RequestScreenshot(settingsBottomPath,
                        out settingsBottomError);
                int settingsBottomWait = 0;
                while (settingsBottomRequested &&
                    !ScreenshotExists(settingsBottomPath) &&
                    settingsBottomWait++ < 120) yield return null;
                bool settingsBottomExists = settingsBottomRequested &&
                    ScreenshotExists(settingsBottomPath);
                if (settingsBottomExists) passed++; else failed++;
                manifest.Append(settingsBottomName).Append('\t')
                    .Append(settingsBottomExists ? "PASS" : "FAIL")
                    .Append('\t').AppendLine(Tsv(settingsBottomDetail +
                        "; " + (settingsBottomError ?? "")));

                UI.LogDisplaySettingsUI.Close();
                yield return new WaitForSecondsRealtime(0.15f);
                yield return new WaitForEndOfFrame();
                MenuBarButton skillsButton = SkillsPlugin.Instance?
                    .GetComponent<MenuBarButton>();
                UI.ExtraSettingsMenuBarButton extraButton =
                    SkillsPlugin.Instance?
                        .GetComponent<UI.ExtraSettingsMenuBarButton>();
                bool nativeButtons =
                    skillsButton?.UsesNativeHudStyleForTesting == true &&
                    extraButton?.UsesNativeHudStyleForTesting == true &&
                    skillsButton.SiblingIndexForTesting >= 0 &&
                    extraButton.SiblingIndexForTesting ==
                        skillsButton.SiblingIndexForTesting + 1;
                string buttonDetail = "skills=" +
                    (skillsButton?.IconSpriteNameForTesting ?? "missing") +
                    "@" + (skillsButton?.SiblingIndexForTesting ?? -1) +
                    "; extra=" +
                    (extraButton?.IconSpriteNameForTesting ?? "missing") +
                    "@" + (extraButton?.SiblingIndexForTesting ?? -1);
                if (nativeButtons) passed++; else failed++;
                manifest.Append("native_menu_buttons\t")
                    .Append(nativeButtons ? "PASS" : "FAIL").Append('\t')
                    .AppendLine(Tsv(buttonDetail));

                string menuBarName = "menu_bar_buttons.png";
                string menuBarPath = Path.Combine(directory, menuBarName);
                bool menuBarRequested = RequestScreenshot(menuBarPath,
                    out string menuBarError);
                int menuBarWait = 0;
                while (menuBarRequested && !ScreenshotExists(menuBarPath) &&
                    menuBarWait++ < 120) yield return null;
                bool menuBarExists = menuBarRequested &&
                    ScreenshotExists(menuBarPath);
                if (menuBarExists) passed++; else failed++;
                manifest.Append(menuBarName).Append('\t')
                    .Append(menuBarExists ? "PASS" : "FAIL").Append('\t')
                    .AppendLine(Tsv(buttonDetail + "; " +
                        (menuBarError ?? "")));

                const int batchSize = 5;
                for (int start = 0;
                    start < Features.LogDisplayOptions.ColorCategoryCount;
                    start += batchSize)
                {
                    Features.LogDisplayOptions.EmitColorSmokeBatch(start,
                        batchSize);
                    yield return new WaitForSecondsRealtime(0.2f);
                    yield return new WaitForEndOfFrame();
                    string name = $"colors_{start + 1:D2}_to_" +
                        $"{Mathf.Min(start + batchSize,
                            Features.LogDisplayOptions.ColorCategoryCount):D2}.png";
                    string path = Path.Combine(directory, name);
                    bool requested = RequestScreenshot(path, out string error);
                    int wait = 0;
                    while (requested && !ScreenshotExists(path) && wait++ < 120)
                        yield return null;
                    bool exists = requested && ScreenshotExists(path);
                    if (exists) passed++; else failed++;
                    manifest.Append(name).Append('\t')
                        .Append(exists ? "PASS" : "FAIL").Append('\t')
                        .AppendLine(Tsv(error ?? ""));
                }

                Features.LogDisplayOptions.EmitDamageSmokeLines();
                yield return new WaitForSecondsRealtime(0.2f);
                yield return new WaitForEndOfFrame();
                string damageName = "damage_types.png";
                string damagePath = Path.Combine(directory, damageName);
                bool damageRequested = RequestScreenshot(damagePath,
                    out string damageError);
                int damageWait = 0;
                while (damageRequested && !ScreenshotExists(damagePath) &&
                    damageWait++ < 120) yield return null;
                bool damageExists = damageRequested &&
                    ScreenshotExists(damagePath);
                if (damageExists) passed++; else failed++;
                manifest.Append(damageName).Append('\t')
                    .Append(damageExists ? "PASS" : "FAIL").Append('\t')
                    .AppendLine(Tsv(damageError ?? ""));
                manifest.Append("self_test\t")
                    .Append(selfTest ? "PASS" : "FAIL").Append('\t')
                    .AppendLine(Tsv(selfTestDetail));
                if (!selfTest) failed++;
                else passed++;
                manifest.Append("settings_ui_self_test\t")
                    .Append(settingsUiSelfTest ? "PASS" : "FAIL")
                    .Append('\t').AppendLine(Tsv(settingsUiSelfTestDetail));
                if (!settingsUiSelfTest) failed++;
                else passed++;

                SafeFileIO.TryWriteAllTextAtomic(Path.Combine(directory,
                    "manifest.tsv"), manifest.ToString(), out _);
                _lastReportPath = directory;
                ChatHelper.Send(
                    $"<color={(failed == 0 ? "#4CAF50" : "#EF5350")}>" +
                    $"[Test Mode] Extra Settings and log display smoke " +
                    $"{(failed == 0 ? "PASS" : "FAIL")}: " +
                    $"{passed} checks passed, {failed} failed.</color>");
                ChatHelper.Send("<color=#AAAAAA>Evidence: " + directory +
                    "</color>");
            }
            finally
            {
                UI.LogDisplaySettingsUI.Close();
                _logDisplayCaptureRunning = false;
            }
        }

        private static void FindItems(string query)
        {
            query = (query ?? "").Trim();
            if (query.Length < 2)
            {
                ChatHelper.Send(
                    "<color=#EF5350>[Test Mode]</color> Use at least two search characters.");
                return;
            }
            var matches = FindCatalogMatches(query, true);
            if (matches.Count == 0)
            {
                ChatHelper.Send(
                    $"<color=#EF5350>[Test Mode]</color> No item matches '{query}'.");
                return;
            }
            ChatHelper.Send(
                $"<color=#FFD54F>[Test Mode]</color> {matches.Count} match(es) for '{query}':");
            int shown = Mathf.Min(SearchResultLimit, matches.Count);
            for (int i = 0; i < shown; i++)
                ChatHelper.Send(
                    $"  <color=#FFFFFF>{matches[i].Name}</color> " +
                    $"<color=#888888>[{matches[i].Source}]</color>");
            if (matches.Count > shown)
                ChatHelper.Send(
                    $"  <color=#888888>...{matches.Count - shown} more; refine the search.</color>");
        }

        private static void GrantItem(string arguments)
        {
            if (!RequireLoadedWorld()) return;
            ParseNameAndQuantity(arguments, out string query, out int amount);
            if (string.IsNullOrWhiteSpace(query))
            {
                ChatHelper.Send(
                    "<color=#EF5350>[Test Mode]</color> Usage: /cstest item &lt;name&gt; [qty]");
                return;
            }
            var matches = ResolveUniqueCatalogMatch(query, true);
            if (matches == null) return;
            Item item = matches.Item;
            int maximum = item.Stackable ? 1000000 : 1000;
            if (amount < 1 || amount > maximum)
            {
                ChatHelper.Send(
                    $"<color=#EF5350>[Test Mode]</color> Quantity must be 1-{maximum} for this item.");
                return;
            }

            var mutation = Items.BagSystem.BeginExternalMutation();
            try
            {
                if (!Items.ItemFactory.GiveItemInstanceToPlayer(item, amount))
                    throw new InvalidOperationException(
                        "the item could not be added; check available inventory space");
                if (!Items.BagSystem.CommitExternalMutation("test-item-grant", mutation))
                    throw new IOException("the inventory transaction could not be saved");
                ChatHelper.Send(
                    $"<color=#4CAF50>[Test Mode]</color> Granted {amount}x " +
                    $"<color=#FFFFFF>{item.ItemName}</color> [{matches.Source}].");
            }
            catch (Exception ex)
            {
                Items.BagSystem.RollbackExternalMutation(mutation);
                SkillsPlugin.Log.LogError($"Test item grant rolled back: {ex}");
                ChatHelper.Send(
                    $"<color=#EF5350>[Test Mode]</color> Grant rolled back: {ex.Message}");
            }
        }

        private static void SetInventoryItemQuality(string arguments)
        {
            if (!RequireLoadedWorld()) return;
            string[] parts = (arguments ?? "").Split(
                new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length != 2 || !int.TryParse(parts[0], out int slotNumber) ||
                !int.TryParse(parts[1], out int quality) || quality <= 0)
            {
                ChatHelper.Send(
                    "<color=#EF5350>[Test Mode]</color> Usage: /cstest quality &lt;inventory-slot&gt; &lt;positive-value&gt;");
                return;
            }
            Inventory inventory = GameData.PlayerInv;
            int index = slotNumber - 1;
            if (index < 0 || index >= inventory.StoredSlots.Count)
            {
                ChatHelper.Send(
                    $"<color=#EF5350>[Test Mode]</color> Slot must be 1-{inventory.StoredSlots.Count}.");
                return;
            }
            ItemIcon slot = inventory.StoredSlots[index];
            Item item = slot?.MyItem;
            if (!Items.BagSystem.IsDirectInventorySlot(slot) || item == null ||
                item == inventory.Empty || item.Stackable ||
                item.RequiredSlot == Item.SlotType.General ||
                Items.BagSystem.IsBag(item))
            {
                ChatHelper.Send(
                    "<color=#EF5350>[Test Mode]</color> That slot does not contain non-stackable gear.");
                return;
            }

            var mutation = Items.BagSystem.BeginExternalMutation();
            int before = slot.Quantity;
            try
            {
                slot.Quantity = quality;
                slot.UpdateSlotImage();
                inventory.UpdatePlayerInventory();
                if (!Items.BagSystem.CommitExternalMutation(
                    "test-set-item-quality", mutation))
                    throw new IOException(
                        "the quality transaction could not be saved");
                ChatHelper.Send(
                    $"<color=#4CAF50>[Test Mode]</color> {item.ItemName} slot state: {before} → {quality}.");
            }
            catch (Exception ex)
            {
                Items.BagSystem.RollbackExternalMutation(mutation);
                ChatHelper.Send(
                    $"<color=#EF5350>[Test Mode]</color> Quality change rolled back: {ex.Message}");
            }
        }

        private static void GrantRecipeMaterials(string query)
        {
            if (!RequireLoadedWorld()) return;
            var recipe = ResolveUniqueRecipe(query);
            if (recipe == null) return;

            var mutation = Items.BagSystem.BeginExternalMutation();
            try
            {
                for (int i = 0; i < recipe.Materials.Length; i++)
                {
                    Item material = Items.ItemFactory.ResolveItem(recipe.Materials[i]);
                    if (material == null || !Items.ItemFactory.GiveItemInstanceToPlayer(
                        material, recipe.MaterialCounts[i]))
                        throw new InvalidOperationException(
                            $"could not add {recipe.MaterialCounts[i]}x {recipe.Materials[i]}");
                }
                if (!Items.BagSystem.CommitExternalMutation(
                    "test-recipe-materials", mutation))
                    throw new IOException("the material transaction could not be saved");
                ChatHelper.Send(
                    $"<color=#4CAF50>[Test Mode]</color> Granted materials for " +
                    $"<color=#FFFFFF>{recipe.Name}</color>. No recipe or skill state was changed.");
            }
            catch (Exception ex)
            {
                Items.BagSystem.RollbackExternalMutation(mutation);
                SkillsPlugin.Log.LogError($"Test material grant rolled back: {ex}");
                ChatHelper.Send(
                    $"<color=#EF5350>[Test Mode]</color> Material grant rolled back: {ex.Message}");
            }
        }

        private static void SetSkill(string arguments)
        {
            if (!RequireLoadedWorld()) return;
            arguments = (arguments ?? "").Trim();
            int separator = arguments.LastIndexOf(' ');
            if (separator <= 0 || !int.TryParse(arguments.Substring(separator + 1),
                    out int level))
            {
                ChatHelper.Send(
                    "<color=#EF5350>[Test Mode]</color> Usage: /cstest skill &lt;name|all&gt; &lt;0-180&gt;");
                return;
            }
            int max = Mathf.Max(1, SkillsPlugin.CfgGlobalMaxLevel?.Value ?? 180);
            if (level < 0 || level > max)
            {
                ChatHelper.Send(
                    $"<color=#EF5350>[Test Mode]</color> Level must be 0-{max}.");
                return;
            }
            string query = arguments.Substring(0, separator).Trim();
            var changed = new List<(SkillEntry skill, int level, float xp)>();
            if (string.Equals(query, "all", StringComparison.OrdinalIgnoreCase))
            {
                foreach (var skill in SkillsSaveManager.Data.GetAll())
                    changed.Add((skill, skill.Level, skill.CurrentXp));
            }
            else
            {
                SkillEntry skill = ResolveUniqueSkill(query);
                if (skill == null) return;
                changed.Add((skill, skill.Level, skill.CurrentXp));
            }

            foreach (var entry in changed)
            {
                entry.skill.Level = level;
                entry.skill.CurrentXp = 0f;
            }
            if (!SkillsSaveManager.Save())
            {
                foreach (var entry in changed)
                {
                    entry.skill.Level = entry.level;
                    entry.skill.CurrentXp = entry.xp;
                }
                ChatHelper.Send(
                    "<color=#EF5350>[Test Mode]</color> Skill change was reverted because saving failed.");
                return;
            }
            ChatHelper.Send(
                $"<color=#4CAF50>[Test Mode]</color> Set " +
                $"{(changed.Count == 1 ? changed[0].skill.Name : "all skills")} to {level}.");
        }

        private static void SetSkillProgress(string arguments)
        {
            if (!RequireLoadedWorld()) return;
            arguments = (arguments ?? "").Trim();
            int separator = arguments.LastIndexOf(' ');
            if (separator <= 0 || !int.TryParse(arguments.Substring(separator + 1),
                    out int percent) || percent < 0 || percent > 100)
            {
                ChatHelper.Send(
                    "<color=#EF5350>[Test Mode]</color> Usage: /cstest skillprogress &lt;name&gt; &lt;0-100&gt;");
                return;
            }

            SkillEntry skill = ResolveUniqueSkill(arguments.Substring(0, separator).Trim());
            if (skill == null) return;
            if (!skill.IsUnlocked || skill.IsAtCap || skill.XpToNextLevel <= 0f)
            {
                ChatHelper.Send(
                    $"<color=#EF5350>[Test Mode]</color> {skill.Name} must be unlocked and below its current cap.");
                return;
            }

            float previousXp = skill.CurrentXp;
            skill.CurrentXp = skill.XpToNextLevel * (percent / 100f);
            if (!SkillsSaveManager.Save())
            {
                skill.CurrentXp = previousXp;
                ChatHelper.Send(
                    "<color=#EF5350>[Test Mode]</color> Skill progress change was reverted because saving failed.");
                return;
            }

            ChatHelper.Send(
                $"<color=#4CAF50>[Test Mode]</color> Set {skill.Name} next-level progress to {percent}%.");
        }

        private static void SetCooldowns(string value)
        {
            value = (value ?? "").Trim();
            bool enabled;
            if (string.Equals(value, "on", StringComparison.OrdinalIgnoreCase))
                enabled = true;
            else if (string.Equals(value, "off", StringComparison.OrdinalIgnoreCase))
                enabled = false;
            else
            {
                ChatHelper.Send(
                    "<color=#EF5350>[Test Mode]</color> Usage: /cstest cooldowns &lt;on|off&gt;");
                return;
            }
            try
            {
                bool previous = SkillsPlugin.CfgTestCooldowns.Value;
                SkillsPlugin.CfgTestCooldowns.Value = enabled;
                try
                {
                    SkillsPlugin.Instance.Config.Save();
                }
                catch
                {
                    SkillsPlugin.CfgTestCooldowns.Value = previous;
                    throw;
                }
                ChatHelper.Send(
                    $"<color=#4CAF50>[Test Mode]</color> Short utility cooldowns " +
                    $"{(enabled ? "enabled" : "disabled")}.");
            }
            catch (Exception ex)
            {
                ChatHelper.Send(
                    $"<color=#EF5350>[Test Mode]</color> Could not save cooldown setting: {ex.Message}");
            }
        }

        private static void OpenWindow(string name)
        {
            name = (name ?? "").Trim();
            if (string.IsNullOrEmpty(name))
            {
                ChatHelper.Send(
                    "<color=#EF5350>[Test Mode]</color> Usage: /cstest open &lt;skills|tradeskill&gt;");
                return;
            }
            if (string.Equals(name, "skills", StringComparison.OrdinalIgnoreCase))
            {
                SkillsUI.ToggleWindow();
                return;
            }
            foreach (string tradeskill in TradeskillNames)
            {
                if (tradeskill.IndexOf(name, StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    UI.TradeskillWindow.Open(tradeskill);
                    return;
                }
            }
            ChatHelper.Send(
                "<color=#EF5350>[Test Mode]</color> Window must be skills, smithing, " +
                "baking, brewing, fletching, jewelcraft, or tailoring.");
        }

        private static void OpenTradeskillQaPreview(string arguments)
        {
            if (!RequireLoadedWorld()) return;
            string[] parts = (arguments ?? "").Split(
                new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length < 1 || parts.Length > 2)
            {
                ChatHelper.Send(
                    "<color=#EF5350>[Test Mode]</color> Usage: /cstest tradeui &lt;tradeskill&gt; &lt;top|bottom&gt;");
                return;
            }

            string tradeskill = TradeskillNames.FirstOrDefault(value =>
                value.IndexOf(parts[0], StringComparison.OrdinalIgnoreCase) >= 0);
            bool bottom = parts.Length == 2 && string.Equals(parts[1],
                "bottom", StringComparison.OrdinalIgnoreCase);
            if (tradeskill == null || (parts.Length == 2 && !bottom &&
                    !string.Equals(parts[1], "top",
                        StringComparison.OrdinalIgnoreCase)))
            {
                ChatHelper.Send(
                    "<color=#EF5350>[Test Mode]</color> Use a valid tradeskill and top or bottom.");
                return;
            }

            if (UI.TradeskillWindow.OpenQaPreview(tradeskill, bottom,
                    out string detail))
                ChatHelper.Send(
                    $"<color=#4CAF50>[Test Mode]</color> Tradeskill UI preview: {detail}.");
            else
                ChatHelper.Send(
                    $"<color=#EF5350>[Test Mode]</color> Tradeskill UI preview failed: {detail}.");
        }

        private static void OpenRecipeVendorBoundary(string arguments)
        {
            if (!RequireLoadedWorld()) return;
            string view = (arguments ?? "recipes").Trim().ToLowerInvariant();
            if (view != "" && view != "stock" && view != "recipes")
            {
                ChatHelper.Send(
                    "<color=#EF5350>[Test Mode]</color> Usage: /cstest vendor &lt;stock|recipes&gt;");
                return;
            }
            VendorInventory vendor = UnityEngine.Object
                .FindObjectsOfType<VendorInventory>()
                .Where(value => value != null && value.gameObject.activeInHierarchy &&
                    value.GetComponent<NPC>() != null &&
                    value.GetComponent<Necromancer.NecromancerVendorMarker>() == null &&
                    value.ItemsForSale != null && value.ItemsForSale.Count > 0)
                .OrderByDescending(value => value.ItemsForSale.Count)
                .ThenBy(value => value.transform.name,
                    StringComparer.OrdinalIgnoreCase)
                .FirstOrDefault();
            if (vendor == null || GameData.VendorWindow == null)
            {
                ChatHelper.Send(
                    "<color=#EF5350>[Test Mode]</color> No standard merchant is ready in this scene.");
                return;
            }

            Stations.RecipeVendors.InjectRecipesIntoVendor(
                vendor.ItemsForSale, vendor);
            int firstRecipe = vendor.ItemsForSale.FindIndex(
                Stations.RecipeVendors.IsRecipeScroll);
            int recipeCount = vendor.ItemsForSale.Count(
                Stations.RecipeVendors.IsRecipeScroll);
            bool ordered = firstRecipe > 0 && recipeCount > 0 &&
                !vendor.ItemsForSale.Take(firstRecipe).Any(
                    Stations.RecipeVendors.IsRecipeScroll) &&
                vendor.ItemsForSale.Skip(firstRecipe).All(
                    Stations.RecipeVendors.IsRecipeScroll);
            if (!ordered)
            {
                ChatHelper.Send(
                    $"<color=#EF5350>[Test Mode]</color> Merchant recipes-last visual FAIL: firstRecipe={firstRecipe}; recipes={recipeCount}; total={vendor.ItemsForSale.Count}.");
                return;
            }

            int recipePage = firstRecipe / 12;
            int stockPage = firstRecipe % 12 == 0
                ? Mathf.Max(0, recipePage - 1) : recipePage;
            int page = view == "stock" ? stockPage : recipePage;
            CharacterController controller = GameData.PlayerControl
                .GetComponent<CharacterController>();
            if (controller != null) controller.enabled = false;
            GameData.PlayerControl.transform.position =
                vendor.transform.position - vendor.transform.forward * 2f;
            if (controller != null) controller.enabled = true;
            GameData.VendorWindow.Page = page;
            GameData.VendorWindow.LoadWindow(vendor.ItemsForSale, vendor);
            int pageStart = page * 12;
            int pageEnd = Mathf.Min(vendor.ItemsForSale.Count, pageStart + 12);
            ChatHelper.Send(
                $"<color=#4CAF50>[Test Mode]</color> Merchant recipes-last visual PASS: " +
                $"{firstRecipe} normal/material items, then {recipeCount} recipes; " +
                $"showing {view} page {page + 1} (items {pageStart + 1}-{pageEnd}) at " +
                $"{vendor.transform.name}.");
        }

        private static void TestCorpseRun(string arguments)
        {
            string verb = (arguments ?? "").Trim().ToLowerInvariant();
            if (verb == "die")
            {
                Features.CorpseRunSystem.TriggerDeathForTesting();
                return;
            }
            if (verb == "redie")
            {
                Features.CorpseRunSystem.TriggerDeathForTesting(
                    allowOutstandingCorpse: true);
                return;
            }
            if (verb == "status" || string.IsNullOrEmpty(verb))
            {
                Features.CorpseRunSystem.HandleChatCommand("/corpserun status");
                return;
            }

            ChatHelper.Send(
                "<color=#EF5350>[Test Mode]</color> Usage: /cstest corpse &lt;die|redie|status&gt;");
        }

        private static void TestInventoryCapacity(string arguments)
        {
            if (!RequireLoadedWorld()) return;
            string verb = (arguments ?? "").Trim().ToLowerInvariant();
            Inventory inventory = GameData.PlayerInv;
            Item filler = Items.ItemFactory.ResolveItem(CapacityFillerItemName);
            if (inventory?.StoredSlots == null || inventory.Empty == null ||
                filler == null || filler.Stackable)
            {
                ChatHelper.Send(
                    "<color=#EF5350>[Test Mode]</color> Capacity-test inventory or filler is unavailable.");
                return;
            }

            int fillerCount = 0;
            int emptyCount = 0;
            foreach (ItemIcon slot in inventory.StoredSlots)
            {
                if (slot == null || slot.TrashSlot) continue;
                if (ReferenceEquals(slot.MyItem, inventory.Empty)) emptyCount++;
                else if (ReferenceEquals(slot.MyItem, filler) ||
                    string.Equals(slot.MyItem?.ItemName, CapacityFillerItemName,
                        StringComparison.Ordinal)) fillerCount++;
            }

            if (string.IsNullOrEmpty(verb) || verb == "status")
            {
                ChatHelper.Send(
                    $"<color=#FFD54F>[Test Mode]</color> Capacity: empty={emptyCount}, " +
                    $"test fillers={fillerCount}, slots={inventory.StoredSlots.Count}.");
                return;
            }

            if (verb != "fill" && verb != "clear")
            {
                ChatHelper.Send(
                    "<color=#EF5350>[Test Mode]</color> Usage: /cstest capacity &lt;fill|clear|status&gt;");
                return;
            }
            if (verb == "fill" && fillerCount > 0)
            {
                ChatHelper.Send(
                    "<color=#EF5350>[Test Mode]</color> Capacity fillers already exist; clear them before another fill.");
                return;
            }
            if (verb == "fill" && emptyCount == 0)
            {
                ChatHelper.Send(
                    "<color=#FFD54F>[Test Mode]</color> Direct inventory is already full.");
                return;
            }
            if (verb == "clear" && fillerCount == 0)
            {
                ChatHelper.Send(
                    "<color=#FFD54F>[Test Mode]</color> No capacity-test fillers were found.");
                return;
            }

            var mutation = Items.BagSystem.BeginExternalMutation();
            try
            {
                int changed;
                if (verb == "fill")
                {
                    changed = emptyCount;
                    if (!Items.ItemFactory.GiveItemInstanceToPlayer(
                        filler, changed))
                        throw new InvalidOperationException(
                            "the direct inventory could not be filled exactly");
                }
                else
                {
                    changed = 0;
                    foreach (ItemIcon slot in inventory.StoredSlots)
                    {
                        if (slot == null || slot.TrashSlot ||
                            (!ReferenceEquals(slot.MyItem, filler) &&
                            !string.Equals(slot.MyItem?.ItemName,
                                CapacityFillerItemName,
                                StringComparison.Ordinal))) continue;
                        slot.MyItem = inventory.Empty;
                        slot.Quantity = 1;
                        slot.UpdateSlotImage();
                        changed++;
                    }
                    inventory.UpdatePlayerInventory();
                }

                if (!Items.BagSystem.CommitExternalMutation(
                    "test-capacity-" + verb, mutation))
                    throw new IOException(
                        "the capacity-test transaction could not be saved");
                ChatHelper.Send(
                    $"<color=#4CAF50>[Test Mode]</color> Capacity {verb}: " +
                    $"{changed} {CapacityFillerItemName} item(s); " +
                    $"{(verb == "fill" ? "inventory is full" : "fillers removed")}.");
            }
            catch (Exception ex)
            {
                Items.BagSystem.RollbackExternalMutation(mutation);
                SkillsPlugin.Log.LogError(
                    $"Capacity test '{verb}' rolled back: {ex}");
                ChatHelper.Send(
                    $"<color=#EF5350>[Test Mode]</color> Capacity {verb} rolled back: {ex.Message}");
            }
        }

        private static void TestPetDamage(string arguments)
        {
            if (!RequireLoadedWorld()) return;
            string remainder = (arguments ?? "").Trim();
            string verb = TakeFirstToken(ref remainder).ToLowerInvariant();
            Character player = GameData.PlayerControl?.Myself;
            if (player == null)
            {
                ChatHelper.Send(
                    "<color=#EF5350>[Test Mode]</color> Player is not ready.");
                return;
            }

            if (verb == "summon")
            {
                if (player.MyCharmedNPC != null)
                {
                    ChatHelper.Send(
                        "<color=#FFD54F>[Test Mode]</color> A pet/charmed NPC is already active. Use pet status.");
                    return;
                }
                Spell[] spells = GameData.SpellDatabase?.SpellDatabase;
                Spell selected = null;
                if (spells != null)
                {
                    foreach (Spell spell in spells)
                    {
                        if (spell == null || spell.Type != Spell.SpellType.Pet ||
                            spell.PetToSummon == null) continue;
                        if (!string.IsNullOrWhiteSpace(remainder) &&
                            (spell.SpellName ?? "").IndexOf(remainder,
                                StringComparison.OrdinalIgnoreCase) < 0) continue;
                        if (selected == null || spell.ManaCost < selected.ManaCost ||
                            (spell.ManaCost == selected.ManaCost &&
                             string.Compare(spell.SpellName, selected.SpellName,
                                 StringComparison.OrdinalIgnoreCase) < 0))
                            selected = spell;
                    }
                }
                if (selected == null)
                {
                    ChatHelper.Send(
                        "<color=#EF5350>[Test Mode]</color> No matching native pet spell was found.");
                    return;
                }
                if (player.MyStats.GetCurrentMana() < selected.ManaCost)
                {
                    ChatHelper.Send(
                        $"<color=#EF5350>[Test Mode]</color> {selected.SpellName} needs " +
                        $"{selected.ManaCost} mana; restore mana and retry.");
                    return;
                }
                bool started = player.MySpells != null &&
                    player.MySpells.StartSpell(selected, player.MyStats, 0f);
                ChatHelper.Send(started
                    ? $"<color=#4CAF50>[Test Mode]</color> Casting native pet spell {selected.SpellName}."
                    : $"<color=#EF5350>[Test Mode]</color> Could not cast {selected.SpellName}.");
                return;
            }

            NPC pet = player.MyCharmedNPC;
            Character petCharacter = pet?.GetChar();
            if (verb == "status" || string.IsNullOrEmpty(verb))
            {
                bool owned = Skills.MagicSkills.IsPlayerSummonedPet(petCharacter);
                ChatHelper.Send(
                    $"<color=#FFD54F>[Test Mode]</color> Pet=" +
                    $"{(pet != null ? pet.NPCName : "none")}; ownedSummon={owned}; " +
                    $"Conjuration={Skills.MagicSkills.GetConjurationMultiplier():F3}x.");
                ChatHelper.Send(
                    $"<color=#AAAAAA>Physical applications=" +
                    $"{Patches.Patch_ConjurationPetPhysicalDamage.ApplicationsForTesting}, " +
                    $"last={Patches.Patch_ConjurationPetPhysicalDamage.LastIncomingForTesting}+" +
                    $"{Patches.Patch_ConjurationPetPhysicalDamage.LastBonusForTesting} -> " +
                    $"{Patches.Patch_ConjurationPetPhysicalDamage.LastScaledIncomingForTesting}+" +
                    $"{Patches.Patch_ConjurationPetPhysicalDamage.LastScaledBonusForTesting}; " +
                    $"spell applications={Patches.Patch_EvocationDamage.PetApplicationsForTesting}, " +
                    $"last={Patches.Patch_EvocationDamage.LastPetIncomingForTesting} -> " +
                    $"{Patches.Patch_EvocationDamage.LastPetScaledForTesting}</color>");
                return;
            }

            if (verb == "probe")
            {
                if (!Skills.MagicSkills.IsPlayerSummonedPet(petCharacter))
                {
                    ChatHelper.Send(
                        "<color=#EF5350>[Test Mode]</color> No player-owned summoned pet is active.");
                    return;
                }
                float multiplier = Skills.MagicSkills
                    .GetConjurationMultiplier();
                int physical = 100;
                int bonus = 25;
                int magic = 100;
                int baseMagic = 80;
                int expectedPhysical = Skills.MagicSkills.ScalePositiveDamage(
                    physical, multiplier);
                int expectedBonus = Skills.MagicSkills.ScalePositiveDamage(
                    bonus, multiplier);
                int expectedMagic = Skills.MagicSkills.ScalePositiveDamage(
                    magic, multiplier);
                int expectedBaseMagic = Skills.MagicSkills.ScalePositiveDamage(
                    baseMagic, multiplier);
                Patches.Patch_ConjurationPetPhysicalDamage.ResetProbeForTesting();
                Patches.Patch_EvocationDamage.ResetPetProbeForTesting();
                Patches.Patch_ConjurationPetPhysicalDamage.Prefix(
                    ref physical, petCharacter, ref bonus);
                Patches.Patch_EvocationDamage.Prefix(
                    ref magic, false, petCharacter, ref baseMagic);
                bool passed = physical == expectedPhysical &&
                    bonus == expectedBonus && magic == expectedMagic &&
                    baseMagic == expectedBaseMagic &&
                    Patches.Patch_ConjurationPetPhysicalDamage.ApplicationsForTesting == 1 &&
                    Patches.Patch_EvocationDamage.PetApplicationsForTesting == 1;
                ChatHelper.Send(
                    $"<color={(passed ? "#4CAF50" : "#EF5350")}>[Test Mode] " +
                    $"Conjuration pet damage probe {(passed ? "PASS" : "FAIL")}: " +
                    $"physical 100+25 -> {physical}+{bonus}; " +
                    $"magic 100/base 80 -> {magic}/base {baseMagic}; " +
                    $"multiplier={multiplier:F3}x.</color>");
                return;
            }

            if (verb == "attack")
            {
                if (!Skills.MagicSkills.IsPlayerSummonedPet(petCharacter))
                {
                    ChatHelper.Send(
                        "<color=#EF5350>[Test Mode]</color> No player-owned summoned pet is active.");
                    return;
                }
                Character target = GameData.PlayerControl.CurrentTarget;
                if (target == null || target == player || target == petCharacter ||
                    target.MyFaction == Character.Faction.Player ||
                    target.MyFaction == Character.Faction.PC)
                {
                    ChatHelper.Send(
                        "<color=#EF5350>[Test Mode]</color> Target a hostile NPC first.");
                    return;
                }
                Patches.Patch_ConjurationPetPhysicalDamage.ResetProbeForTesting();
                Patches.Patch_EvocationDamage.ResetPetProbeForTesting();
                pet.CurrentAggroTarget = target;
                pet.AggroOn(target);
                ChatHelper.Send(
                    $"<color=#4CAF50>[Test Mode]</color> Ordered {pet.NPCName} to attack " +
                    $"{target.transform.name}. Use /cstest pet status after it lands hits.");
                return;
            }

            ChatHelper.Send(
                "<color=#EF5350>[Test Mode]</color> Usage: /cstest pet &lt;summon [name]|probe|attack|status&gt;");
        }

        private void TestNecromancer(string arguments)
        {
            if (!RequireLoadedWorld()) return;
            string remainder = (arguments ?? "").Trim();
            string verb = TakeFirstToken(ref remainder).ToLowerInvariant();
            Necromancer.NecromancerCatalog.TryInitialize();
            if (verb == "" || verb == "status")
            {
                bool catalog = Necromancer.NecromancerCatalog.ValidateCatalog(
                    out string catalogDetail);
                bool balance = Necromancer.NecromancerCatalog.ValidateBalance(
                    out string balanceDetail);
                bool ascensions = Necromancer.NecromancerAscensions.Validate(
                    out string ascensionDetail);
                bool petGear = Necromancer.NecromancerPetEquipment
                    .RunSerializationSelfTest(out string petGearDetail);
                bool classGear = Necromancer.NecromancerGear
                    .ValidateCatalog(out string classGearDetail);
                bool gearAcquisition = Necromancer.NecromancerGear
                    .RunAcquisitionSelfTest(out string gearAcquisitionDetail);
                bool spellAcquisition = Necromancer.NecromancerSpellLoot
                    .RunAcquisitionSelfTest(out string spellAcquisitionDetail);
                bool vendor = Necromancer.NecromancerVendors
                    .ValidateCurrentSceneVendor(out string vendorDetail);
                bool auraCatalog = Necromancer.NecromancerAuras
                    .ValidateCatalog(out string auraCatalogDetail);
                bool auraMerchant = Necromancer.NecromancerAuras
                    .ValidateStarterMerchant(out string auraMerchantDetail);
                bool sharedSkills = Necromancer.NecromancerSharedSkills
                    .Validate(out string sharedSkillsDetail);
                bool starterWeapons =
                    Necromancer.NecromancerCharacterCreation
                        .ValidateStarterWeapons(out string starterWeaponsDetail);
                Necromancer.NecromancerPetMarker statusPetMarker =
                    Necromancer.NecromancerPets.ActivePet?.GetComponent<
                        Necromancer.NecromancerPetMarker>();
                string petSpeedDetail =
                    "no active bone thrall; synchronizer registered";
                bool petSpeed = statusPetMarker == null ||
                    statusPetMarker.ValidateMovementSpeed(out petSpeedDetail);
                bool qaIdentity = IsNecromancerQaCharacter(out string qaDetail);
                ChatHelper.Send(
                    $"<color={(catalog && balance && ascensions && petGear && classGear && gearAcquisition && spellAcquisition && vendor && auraCatalog && auraMerchant && sharedSkills && starterWeapons && petSpeed && qaIdentity ? "#4CAF50" : "#EF5350")}>" +
                    $"[Test Mode] Necromancer checks: " +
                    $"catalog={(catalog ? "PASS" : "FAIL")}; " +
                    $"balance={(balance ? "PASS" : "FAIL")}; " +
                    $"Ascensions={(ascensions ? "PASS" : "FAIL")}; " +
                    $"pet gear={(petGear ? "PASS" : "FAIL")}; " +
                    $"class gear={(classGear && gearAcquisition ? "PASS" : "FAIL")}; " +
                    $"epic drops={(spellAcquisition ? "PASS" : "FAIL")}; " +
                    $"vendor={(vendor ? "PASS" : "FAIL")}; " +
                    $"auras={(auraCatalog && auraMerchant ? "PASS" : "FAIL")}; " +
                    $"shared skills={(sharedSkills ? "PASS" : "FAIL")}; " +
                    $"starter weapons={(starterWeapons ? "PASS" : "FAIL")}; " +
                    $"pet speed={(petSpeed ? "PASS" : "FAIL")}; " +
                    $"QA={(qaIdentity ? "PASS" : "FAIL")}.</color>");
                ChatHelper.Send("<color=#AAAAAA>" + catalogDetail + "</color>");
                ChatHelper.Send("<color=#AAAAAA>" + balanceDetail + "</color>");
                ChatHelper.Send("<color=#AAAAAA>" + ascensionDetail + "; " +
                    qaDetail + "</color>");
                ChatHelper.Send("<color=#AAAAAA>" + petGearDetail + "; " +
                    classGearDetail + "; " + gearAcquisitionDetail + "; " +
                    spellAcquisitionDetail + "; " + vendorDetail + "; " +
                    auraCatalogDetail + "; " +
                    auraMerchantDetail + "; " + sharedSkillsDetail + "; " +
                    starterWeaponsDetail + "; " + petSpeedDetail + "; live=" +
                    Necromancer.NecromancerPetEquipment.Describe() + "</color>");
                return;
            }
            if (verb == "fresh" || verb == "freshstate")
            {
                FreshCharacterState.ClearIneligibleLoadedAura(
                    out string migrationDetail);
                bool aura = FreshCharacterState.RunAuraLeakSelfTest(
                    out string auraDetail);
                bool bindings = Necromancer.NecromancerCatalog
                    .ValidateCatalog(out string bindingDetail);
                bool passed = aura && bindings;
                ChatHelper.Send(
                    $"<color={(passed ? "#4CAF50" : "#EF5350")}>" +
                    $"[Test Mode] Fresh Necromancer lifecycle " +
                    $"{(passed ? "PASS" : "FAIL")}.</color>");
                ChatHelper.Send("<color=#AAAAAA>Aura isolation: " +
                    migrationDetail + "; " + auraDetail +
                    "; spell/VFX bindings: " +
                    bindingDetail + ".</color>");
                return;
            }
            if (verb == "skills" || verb == "sharedskills")
            {
                bool passed = Necromancer.NecromancerSharedSkills
                    .RunInGameUseSelfTest(out string detail);
                ChatHelper.Send(
                    $"<color={(passed ? "#4CAF50" : "#EF5350")}>" +
                    $"[Test Mode] Necromancer shared Arcanist combat skills " +
                    $"{(passed ? "PASS" : "FAIL")}.</color>");
                ChatHelper.Send("<color=#AAAAAA>" + detail + "</color>");
                return;
            }
            if (verb == "epic" || verb == "drops" || verb == "epicdrops")
            {
                bool passed = Necromancer.NecromancerSpellLoot
                    .RunAcquisitionSelfTest(out string detail);
                ChatHelper.Send(
                    $"<color={(passed ? "#4CAF50" : "#EF5350")}>" +
                    $"[Test Mode] Necromancer epic spell acquisition " +
                    $"{(passed ? "PASS" : "FAIL")}.</color>");
                ChatHelper.Send("<color=#AAAAAA>" + detail + "</color>");
                return;
            }
            if (verb == "dps" || verb == "benchmark")
            {
                if (!RequireNecromancerQaCharacter()) return;
                if (_necromancerCastRunning || _necromancerVfxRunning ||
                    _necromancerPetRoleRunning ||
                    _smokeRunning || Level35DpsBenchmark.IsRunning)
                {
                    ChatHelper.Send(
                        "<color=#FFD54F>[Test Mode]</color> Another visual, smoke, or DPS run is already active.");
                    return;
                }
                if (GameData.InCombat || GameData.Autoattacking)
                {
                    ChatHelper.Send(
                        "<color=#EF5350>[Test Mode]</color> Leave combat and stop autoattack before starting the DPS benchmark.");
                    return;
                }
                if (GameData.PlayerControl?.Myself?.MyCharmedNPC != null)
                {
                    ChatHelper.Send(
                        "<color=#EF5350>[Test Mode]</color> Dismiss or reclaim the active pet before the controlled DPS benchmark.");
                    return;
                }
                if (Necromancer.NecromancerPetEquipment.HasGear)
                {
                    ChatHelper.Send(
                        "<color=#EF5350>[Test Mode]</color> Remove the active pet escrow weapon and shield before the standardized benchmark; the harness supplies its own transient test weapon.");
                    return;
                }
                string mode = string.IsNullOrWhiteSpace(remainder)
                    ? "full" : remainder.Trim();
                StartCoroutine(Level35DpsBenchmark.Run(mode));
                return;
            }
            if (verb == "class")
            {
                if (!RequireNecromancerQaCharacter()) return;
                GameData.PlayerStats.CharacterClass =
                    Necromancer.NecromancerCatalog.NecromancerClass;
                GameData.CurrentCharacterSlot.CharClass =
                    Necromancer.NecromancerCatalog.ClassName;
                GameData.PlayerStats.CalcStats();
                GameData.GM.SaveGameData(wScene: false);
                ChatHelper.Send(
                    "<color=#4CAF50>[Test Mode]</color> Test character class set to Necromancer and saved.");
                return;
            }
            if (verb == "level")
            {
                if (!RequireNecromancerQaCharacter()) return;
                if (!int.TryParse(remainder.Trim(), out int level) ||
                    level < 1 || level > 35)
                {
                    ChatHelper.Send(
                        "<color=#EF5350>[Test Mode]</color> Necromancer QA level must be 1-35.");
                    return;
                }
                GameData.PlayerStats.Level = level;
                GameData.CurrentCharacterSlot.CharLevel = level;
                GameData.PlayerStats.CalcStats();
                GameData.PlayerStats.CurrentHP =
                    GameData.PlayerStats.CurrentMaxHP;
                GameData.PlayerStats.CurrentMana =
                    GameData.PlayerStats.GetCurrentMaxMana();
                GameData.GM.SaveGameData(wScene: false);
                ChatHelper.Send(
                    $"<color=#4CAF50>[Test Mode]</color> John EverQuest is level {level}; stats and save data were refreshed.");
                return;
            }
            if (verb == "learn")
            {
                if (!RequireNecromancerQaCharacter()) return;
                List<Spell> known = GameData.PlayerControl?.Myself?.MySpells?.KnownSpells;
                if (known == null)
                {
                    ChatHelper.Send(
                        "<color=#EF5350>[Test Mode]</color> Player spellbook is unavailable.");
                    return;
                }
                int added = 0;
                foreach (Spell spell in Necromancer.NecromancerCatalog
                    .GetLearnableSpells())
                    if (!known.Contains(spell))
                    {
                        known.Add(spell);
                        added++;
                    }
                GameData.GM.SaveGameData(wScene: false);
                ChatHelper.Send(
                    $"<color=#4CAF50>[Test Mode]</color> Learned {added} missing " +
                    $"Necromancer spells ({known.Count} total known).");
                return;
            }
            if (verb == "cast")
            {
                if (!RequireActiveNecromancerForReadOnlyQa()) return;
                Spell spell = ResolveNecromancerSpell(remainder);
                if (spell == null) return;
                // A controlled QA cast must be able to exercise the level-32
                // pet even when John's ordinary mana pool is below its cost.
                // This changes only the dedicated opt-in test character.
                if (GameData.PlayerStats.GetCurrentMana() < spell.ManaCost)
                    GameData.PlayerStats.CurrentMana = spell.ManaCost;
                Stats target = GameData.PlayerControl?.CurrentTarget?.MyStats;
                if (spell.SelfOnly || spell.Type == Spell.SpellType.Pet)
                    target = GameData.PlayerStats;
                bool started = GameData.PlayerControl?.Myself?.MySpells?
                    .StartSpell(spell, target) == true;
                ChatHelper.Send(
                    $"<color={(started ? "#4CAF50" : "#EF5350")}>[Test Mode] " +
                    $"{(started ? "Casting" : "Could not cast")} {spell.SpellName}.</color>");
                return;
            }
            if (verb == "castall")
            {
                if (!RequireNecromancerQaCharacter()) return;
                if (_necromancerCastRunning || _necromancerVfxRunning ||
                    _smokeRunning)
                {
                    ChatHelper.Send(
                        "<color=#FFD54F>[Test Mode]</color> Another visual or smoke run is already active.");
                    return;
                }
                if (Necromancer.NecromancerPets.ActivePet != null)
                {
                    ChatHelper.Send(
                        "<color=#EF5350>[Test Mode]</color> Dismiss or reclaim the active pet before the controlled cast sweep.");
                    return;
                }
                StartCoroutine(RunNecromancerCastSweep());
                return;
            }
            if (verb == "petspeed")
            {
                if (!RequireActiveNecromancerForReadOnlyQa()) return;
                Necromancer.NecromancerPetMarker marker =
                    Necromancer.NecromancerPets.ActivePet?.GetComponent<
                        Necromancer.NecromancerPetMarker>();
                if (marker == null)
                {
                    ChatHelper.Send(
                        "<color=#EF5350>[Test Mode]</color> Summon a Bone Thrall before checking pet speed.");
                    return;
                }
                marker.SynchronizeMovementSpeed();
                bool synchronized = marker.ValidateMovementSpeed(
                    out string speedDetail);
                ChatHelper.Send(
                    $"<color={(synchronized ? "#4CAF50" : "#EF5350")}>" +
                    $"[Test Mode] Bone Thrall movement speed " +
                    $"{(synchronized ? "PASS" : "FAIL")}: " +
                    speedDetail + ".</color>");
                return;
            }
            if (verb == "vfx")
            {
                if (!RequireNecromancerQaCharacter()) return;
                if (_necromancerVfxRunning)
                {
                    ChatHelper.Send(
                        "<color=#FFD54F>[Test Mode]</color> A Necromancer VFX preview is already running.");
                    return;
                }
                bool all = string.Equals(remainder.Trim(), "all",
                    StringComparison.OrdinalIgnoreCase) ||
                    string.IsNullOrWhiteSpace(remainder);
                Spell one = all ? null : ResolveNecromancerSpell(remainder);
                if (!all && one == null) return;
                StartCoroutine(RunNecromancerVfxPreview(one));
                return;
            }
            if (verb == "icon" || verb == "icons")
            {
                if (_necromancerIconCaptureRunning ||
                    _craftedIconCaptureRunning ||
                    _necromancerVendorCaptureRunning || _smokeRunning ||
                    _gearVisualRunning || _necromancerVfxRunning ||
                    _necromancerCastRunning || _logDisplayCaptureRunning)
                {
                    ChatHelper.Send(
                        "<color=#FFD54F>[Test Mode]</color> Another visual or smoke capture is already active.");
                    return;
                }
                StartCoroutine(CaptureNecromancerIconCatalog());
                return;
            }
            if (verb == "aura" || verb == "auras")
            {
                if (!RequireNecromancerQaCharacter()) return;
                string action = (remainder ?? "").Trim().ToLowerInvariant();
                if (action == "verify" || action == "reload")
                {
                    bool persisted = Necromancer.NecromancerAuras
                        .ValidateEquippedPersistence(out string persistDetail);
                    ChatHelper.Send(
                        $"<color={(persisted ? "#4CAF50" : "#EF5350")}>" +
                        $"[Test Mode] Necromancer aura reload " +
                        $"{(persisted ? "PASS" : "FAIL")}: " +
                        persistDetail + ".</color>");
                    return;
                }
                if (_necromancerAuraTestRunning ||
                    _necromancerIconCaptureRunning ||
                    _craftedIconCaptureRunning ||
                    _necromancerVendorCaptureRunning || _smokeRunning ||
                    _gearVisualRunning || _necromancerVfxRunning ||
                    _necromancerCastRunning || _logDisplayCaptureRunning)
                {
                    ChatHelper.Send(
                        "<color=#FFD54F>[Test Mode]</color> Another visual, aura, or smoke capture is already active.");
                    return;
                }
                StartCoroutine(RunNecromancerAuraTest());
                return;
            }
            if (verb == "gear" || verb == "classgear")
            {
                if (!RequireNecromancerQaCharacter()) return;
                string action = (remainder ?? "").Trim().ToLowerInvariant();
                if (action == "verify" || action == "reload")
                {
                    bool persisted = Necromancer.NecromancerGear
                        .ValidateEquippedPersistence(out string persistDetail);
                    ChatHelper.Send(
                        $"<color={(persisted ? "#4CAF50" : "#EF5350")}>" +
                        $"[Test Mode] Necromancer class gear reload " +
                        $"{(persisted ? "PASS" : "FAIL")}: " +
                        persistDetail + ".</color>");
                    return;
                }
                if (_necromancerGearTestRunning ||
                    _necromancerAuraTestRunning ||
                    _necromancerIconCaptureRunning ||
                    _craftedIconCaptureRunning ||
                    _necromancerVendorCaptureRunning || _smokeRunning ||
                    _gearVisualRunning || _necromancerVfxRunning ||
                    _necromancerCastRunning || _logDisplayCaptureRunning)
                {
                    ChatHelper.Send(
                        "<color=#FFD54F>[Test Mode]</color> Another visual, gear, or smoke capture is already active.");
                    return;
                }
                StartCoroutine(RunNecromancerGearTest());
                return;
            }
            if (verb == "ascension" || verb == "ascensions")
            {
                if (!RequireNecromancerQaCharacter()) return;
                string action = (remainder ?? "").Trim().ToLowerInvariant();
                UseSkill skills = GameData.PlayerControl?.Myself?.MySkills;
                AAScreen screen = GameData.GM?.AscensionWindow
                    ?.GetComponent<AAScreen>();
                if (skills == null || screen == null)
                {
                    ChatHelper.Send(
                        "<color=#EF5350>[Test Mode]</color> The native Ascension UI is unavailable.");
                    return;
                }
                if (action == "grant")
                {
                    skills.AscensionPoints = Mathf.Max(
                        skills.AscensionPoints, 23);
                    GameData.GM.SaveGameData(wScene: false);
                }
                GameData.PlayerInv.ForceOpenInv();
                GameData.GM.InvItemPanel.SetActive(false);
                GameData.GM.CosmeticWindow.SetActive(false);
                GameData.GM.AscensionWindow.SetActive(true);
                if (action == "exercise")
                {
                    skills.AscensionPoints = Mathf.Max(
                        skills.AscensionPoints, 40);
                    screen.points = skills.AscensionPoints;
                    screen.SetGeneral();
                    Ascension[] general = GameData.SkillDatabase
                        .AscensionDatabase.Where(value => value != null &&
                            value.UsedBy == Ascension.Class.General).ToArray();
                    int generalSpent = general.Sum(value =>
                        skills.GetAscensionRank(value.Id));
                    var exerciseErrors = new List<string>();
                    foreach (Ascension perk in general)
                    {
                        while (generalSpent < 8 &&
                            skills.GetAscensionRank(perk.Id) < perk.MaxRank)
                        {
                            int beforeRank = skills.GetAscensionRank(perk.Id);
                            screen.points = skills.AscensionPoints;
                            screen.AddPointToAscension(perk.Id);
                            int afterRank = skills.GetAscensionRank(perk.Id);
                            if (afterRank != beforeRank + 1)
                            {
                                exerciseErrors.Add(
                                    $"general allocation stalled on {perk.SkillName}");
                                break;
                            }
                            generalSpent++;
                        }
                        if (generalSpent >= 8) break;
                    }
                    screen.SetClassSpecific();
                    foreach (Ascension perk in Necromancer.NecromancerAscensions.All)
                    {
                        if (skills.GetAscensionRank(perk.Id) > 0) continue;
                        int beforeRank = skills.GetAscensionRank(perk.Id);
                        screen.points = skills.AscensionPoints;
                        screen.AddPointToAscension(perk.Id);
                        int afterRank = skills.GetAscensionRank(perk.Id);
                        if (afterRank != beforeRank + 1)
                            exerciseErrors.Add(
                                $"class allocation stalled on {perk.SkillName}");
                    }
                    screen.points = skills.AscensionPoints;
                    screen.SetClassSpecific();
                    bool allAllocated = generalSpent >= 8 &&
                        Necromancer.NecromancerAscensions.All.All(perk =>
                            skills.GetAscensionRank(perk.Id) >= 1) &&
                        exerciseErrors.Count == 0;
                    GameData.GM.SaveGameData(wScene: false);
                    ChatHelper.Send(
                        $"<color={(allAllocated ? "#4CAF50" : "#EF5350")}>" +
                        $"[Test Mode] Native Ascension allocation " +
                        $"{(allAllocated ? "PASS" : "FAIL")}: general spent=" +
                        $"{generalSpent}; class ranks=" +
                        string.Join(", ", Necromancer.NecromancerAscensions.All
                            .Select(perk => perk.SkillName + " " +
                                skills.GetAscensionRank(perk.Id))) +
                        (exerciseErrors.Count == 0 ? "." : "; " +
                            string.Join("; ", exerciseErrors) + ".") +
                        "</color>");
                }
                screen.SetClassSpecific();
                screen.points = skills.AscensionPoints;
                if (screen.Points != null)
                    screen.Points.text = "Ascension Points: " +
                        skills.AscensionPoints;
                bool valid = Necromancer.NecromancerAscensions.Validate(
                    out string detail);
                int populated = screen.AllAASlots?.Count(slot => slot != null &&
                    Necromancer.NecromancerAscensions.IsCustomId(slot.id)) ?? 0;
                ChatHelper.Send(
                    $"<color={(valid && populated == 8 ? "#4CAF50" : "#EF5350")}>" +
                    $"[Test Mode] Necromancer Ascension tab " +
                    $"{(valid && populated == 8 ? "PASS" : "FAIL")}: " +
                    $"{populated}/8 slots; unspent={skills.AscensionPoints}. " +
                    $"{detail}</color>");
                return;
            }
            if (verb == "petgear")
            {
                string petGearArguments = (remainder ?? "").Trim();
                string action = TakeFirstToken(ref petGearArguments)
                    .ToLowerInvariant();
                bool readOnlyAction = action == "inspect" ||
                    action == "motion" || action == "combat" ||
                    action == "rigs" || action == "rigaudit" ||
                    action == "playerig" || action == "playerrig" ||
                    string.IsNullOrEmpty(action);
                if (readOnlyAction)
                {
                    if (!RequireActiveNecromancerForReadOnlyQa()) return;
                }
                else if (!RequireNecromancerQaCharacter()) return;
                if (action == "return")
                {
                    bool returned = Necromancer.NecromancerPetEquipment
                        .ReturnAllToPlayer(out string detail);
                    ChatHelper.Send(
                        $"<color={(returned ? "#4CAF50" : "#EF5350")}>[Test Mode] " +
                        $"Pet gear return {(returned ? "PASS" : "FAIL")}: {detail}.</color>");
                }
                else if (action == "inspect")
                {
                    StartCoroutine(CaptureNecromancerPetEquipment());
                }
                else if (action == "motion" || action == "combat")
                {
                    if (_necromancerPetMotionRunning ||
                        _necromancerPetRoleRunning ||
                        _necromancerCastRunning ||
                        _necromancerVfxRunning || _smokeRunning)
                    {
                        ChatHelper.Send(
                            "<color=#FFD54F>[Test Mode]</color> Another pet motion, role, visual, or smoke capture is already active.");
                        return;
                    }
                    StartCoroutine(CaptureNecromancerPetEquipmentMotion());
                }
                else if (action == "rigs" || action == "rigaudit")
                {
                    DumpNecromancerPetRigs();
                }
                else if (action == "playerig" || action == "playerrig")
                {
                    DumpPlayerAndThrallRig();
                }
                else if (action == "loadout" || action == "test")
                {
                    string[] loadoutNames = petGearArguments.Split(
                        new[] { '|' }, 2);
                    string primaryName = loadoutNames.Length > 0 &&
                        !string.IsNullOrWhiteSpace(loadoutNames[0])
                            ? loadoutNames[0].Trim()
                            : "Copper Sword";
                    string secondaryName = loadoutNames.Length > 1
                        ? loadoutNames[1].Trim()
                        : (string.IsNullOrWhiteSpace(petGearArguments)
                            ? "Bark Shield" : "");
                    Item weapon = Items.ItemFactory.ResolveItemForTesting(
                        primaryName);
                    Item shield = string.IsNullOrWhiteSpace(secondaryName)
                        ? null
                        : Items.ItemFactory.ResolveItemForTesting(
                            secondaryName);
                    string detail = "";
                    bool resolved = weapon != null &&
                        (string.IsNullOrWhiteSpace(secondaryName) ||
                            shield != null);
                    bool installed = resolved &&
                        Necromancer.NecromancerPetEquipment.InstallTestLoadout(
                            weapon, 1, shield, 1, out detail);
                    if (!resolved)
                        detail = $"Could not resolve QA loadout " +
                            $"'{primaryName}'" +
                            (string.IsNullOrWhiteSpace(secondaryName)
                                ? ""
                                : $" + '{secondaryName}'");
                    ChatHelper.Send(
                        $"<color={(installed ? "#4CAF50" : "#EF5350")}>" +
                        $"[Test Mode] Pet QA loadout " +
                        $"{(installed ? "PASS" : "FAIL")}: {detail}.</color>");
                }
                else
                    ChatHelper.Send(
                        "<color=#FFD54F>[Test Mode]</color> Pet gear: " +
                        Necromancer.NecromancerPetEquipment.Describe());
                return;
            }
            if (verb == "petrole" || verb == "taunt")
            {
                if (!RequireNecromancerQaCharacter()) return;
                if (_necromancerPetRoleRunning || _necromancerCastRunning ||
                    _necromancerVfxRunning || _smokeRunning ||
                    Level35DpsBenchmark.IsRunning)
                {
                    ChatHelper.Send(
                        "<color=#FFD54F>[Test Mode]</color> Another pet-role, visual, smoke, or DPS run is already active.");
                    return;
                }
                StartCoroutine(RunNecromancerPetRoleTest());
                return;
            }
            if (verb == "travel")
            {
                string destination = (remainder ?? "").Trim();
                switch (destination.ToLowerInvariant())
                {
                    case "stowaway":
                        GameData.SceneChange.ChangeScene("Stowaway",
                            new Vector3(831.2f, 16.5f, 473f), true, 0f);
                        break;
                    case "willowwatch":
                        GameData.SceneChange.ChangeScene("Willowwatch",
                            new Vector3(649f, 50f, 388f), true, -90f);
                        break;
                    case "azure":
                    case "port azure":
                        GameData.SceneChange.ChangeScene("Azure",
                            new Vector3(226f, 11f, 455f), true, 180f);
                        break;
                    default:
                        ChatHelper.Send(
                            "<color=#EF5350>[Test Mode]</color> Travel destination must be Stowaway, Willowwatch, or Azure.");
                        return;
                }
                ChatHelper.Send(
                    $"<color=#4CAF50>[Test Mode]</color> Traveling to {destination} for vendor validation.");
                return;
            }
            if (verb == "vendor")
            {
                string action = (remainder ?? "").Trim().ToLowerInvariant();
                if (action == "capture" || action == "audit")
                {
                    if (_necromancerVendorCaptureRunning || _smokeRunning ||
                        _craftedIconCaptureRunning ||
                        _gearVisualRunning || _necromancerVfxRunning ||
                        _necromancerCastRunning || _logDisplayCaptureRunning)
                    {
                        ChatHelper.Send(
                            "<color=#FFD54F>[Test Mode]</color> Another visual or smoke capture is already active.");
                        return;
                    }
                    StartCoroutine(CaptureNecromancerVendor());
                    return;
                }
                Necromancer.NecromancerVendorMarker marker = UnityEngine.Object
                    .FindObjectOfType<Necromancer.NecromancerVendorMarker>();
                VendorInventory vendor = marker != null
                    ? marker.GetComponent<VendorInventory>() : null;
                if (vendor == null || GameData.VendorWindow == null)
                {
                    ChatHelper.Send(
                        "<color=#EF5350>[Test Mode]</color> The current scene's Necromancer vendor is not ready.");
                    return;
                }

                // Keep the real proximity-close behavior active while opening
                // the native merchant window for a controlled visual check.
                CharacterController controller = GameData.PlayerControl
                    .GetComponent<CharacterController>();
                if (controller != null) controller.enabled = false;
                GameData.PlayerControl.transform.position =
                    marker.transform.position - marker.transform.forward * 2f;
                if (controller != null) controller.enabled = true;
                GameData.VendorWindow.Page = 0;
                GameData.VendorWindow.LoadWindow(vendor.ItemsForSale, vendor);
                ChatHelper.Send(
                    $"<color=#4CAF50>[Test Mode]</color> Opened {marker.transform.name} with {vendor.ItemsForSale.Count} spells and auras in the native vendor window.");
                return;
            }
            ChatHelper.Send(
                "<color=#EF5350>[Test Mode]</color> Usage: /cstest necro " +
                "&lt;status|skills|class|level 1-35|learn|cast NAME|castall|vfx [NAME|all]|" +
                "icons|aura [verify]|gear [verify]|ascension [grant|exercise]|petgear [status|loadout|inspect|motion|rigs|return]|" +
                "petrole|epic|travel ZONE|vendor&gt;");
        }

        private static void DumpPlayerAndThrallRig()
        {
            string directory = Path.Combine(RuntimePaths.DataDirectory,
                "TestReports", "NecromancerPlayerRig");
            Directory.CreateDirectory(directory);
            string path = Path.Combine(directory,
                $"{DateTime.UtcNow:yyyyMMdd-HHmmss}-" +
                SafeReportName(SkillsSaveManager.CharName) + ".txt");
            var output = new StringBuilder();
            output.AppendLine("Erenshor player-compatible thrall rig audit");
            output.AppendLine("Character: " + SkillsSaveManager.CharName);
            AppendRuntimeRig(output, "Live player control",
                GameData.PlayerControl != null
                    ? GameData.PlayerControl.gameObject : null);
            AppendRuntimeRig(output, "Live player modular rig",
                GameData.PlayerInv?.Modulars != null
                    ? GameData.PlayerInv.Modulars.gameObject : null);
            AppendRuntimeRig(output, "Player modular template",
                GameData.PlayerModularParent != null
                    ? GameData.PlayerModularParent.gameObject : null);
            AppendRuntimeRig(output, "Active bone thrall",
                Necromancer.NecromancerPets.ActivePet != null
                    ? Necromancer.NecromancerPets.ActivePet.gameObject : null);

            SafeFileIO.TryWriteAllTextAtomic(path, output.ToString(), out _);
            SkillsPlugin.Log.LogInfo(
                "Player-compatible thrall rig audit written: " + path);
            ChatHelper.Send(
                "<color=#4CAF50>[Test Mode]</color> Player/thrall rig audit written.");
            ChatHelper.Send("<color=#AAAAAA>Evidence: " + path + "</color>");
        }

        private static void AppendRuntimeRig(StringBuilder output,
            string label, GameObject root)
        {
            output.AppendLine();
            output.AppendLine("=== " + label + " ===");
            if (root == null)
            {
                output.AppendLine("<unavailable>");
                return;
            }

            Transform[] transforms =
                root.GetComponentsInChildren<Transform>(true);
            Animator[] animators =
                root.GetComponentsInChildren<Animator>(true);
            SkinnedMeshRenderer[] skinned =
                root.GetComponentsInChildren<SkinnedMeshRenderer>(true);
            ModularParts[] modulars =
                root.GetComponentsInChildren<ModularParts>(true);
            output.AppendLine($"Root={root.name}; transforms={transforms.Length}; " +
                $"animators={animators.Length}; skinned={skinned.Length}; " +
                $"modularParts={modulars.Length}");

            foreach (Animator animator in animators)
            {
                RuntimeAnimatorController controller =
                    animator.runtimeAnimatorController;
                output.AppendLine("Animator: " +
                    GetQaTransformPath(animator.transform, root.transform) +
                    $"; enabled={animator.enabled}; human={animator.isHuman}; " +
                    $"avatar={(animator.avatar != null ? animator.avatar.name : "<none>")}; " +
                    $"controller={(controller != null ? controller.name : "<none>")}; " +
                    $"layers={animator.layerCount}; params={animator.parameters.Length}");
                if (animator.isHuman)
                {
                    HumanBodyBones[] bones =
                    {
                        HumanBodyBones.Hips,
                        HumanBodyBones.Spine,
                        HumanBodyBones.Chest,
                        HumanBodyBones.UpperChest,
                        HumanBodyBones.LeftUpperArm,
                        HumanBodyBones.LeftLowerArm,
                        HumanBodyBones.LeftHand,
                        HumanBodyBones.RightUpperArm,
                        HumanBodyBones.RightLowerArm,
                        HumanBodyBones.RightHand
                    };
                    foreach (HumanBodyBones bone in bones)
                        output.AppendLine($"  Bone {bone}: " +
                            GetQaTransformPath(
                                animator.GetBoneTransform(bone),
                                root.transform));
                }
                foreach (AnimatorControllerParameter parameter in
                    animator.parameters)
                    output.AppendLine($"  Param {parameter.name}: " +
                        parameter.type);
                if (controller != null)
                {
                    string[] clips = controller.animationClips
                        .Where(clip => clip != null)
                        .Select(clip => clip.name)
                        .Distinct()
                        .OrderBy(name => name)
                        .ToArray();
                    output.AppendLine("  Clips (" + clips.Length + "): " +
                        string.Join(", ", clips));
                }
            }

            foreach (SkinnedMeshRenderer renderer in skinned)
            {
                string[] boneNames = (renderer.bones ??
                        Array.Empty<Transform>())
                    .Where(bone => bone != null)
                    .Select(bone => bone.name)
                    .Distinct()
                    .OrderBy(name => name)
                    .ToArray();
                output.AppendLine("SkinnedMesh: " +
                    GetQaTransformPath(renderer.transform, root.transform) +
                    $"; enabled={renderer.enabled}; active=" +
                    $"{renderer.gameObject.activeInHierarchy}; rootBone=" +
                    $"{GetQaTransformPath(renderer.rootBone, root.transform)}; " +
                    $"bones={boneNames.Length}: " +
                    string.Join(", ", boneNames));
            }

            foreach (ModularParts modular in modulars)
            {
                output.AppendLine("ModularParts: " +
                    GetQaTransformPath(modular.transform, root.transform));
                foreach (FieldInfo field in typeof(ModularParts).GetFields(
                    BindingFlags.Instance | BindingFlags.Public |
                    BindingFlags.NonPublic))
                {
                    object value;
                    try { value = field.GetValue(modular); }
                    catch { continue; }
                    Transform transform = value as Transform;
                    if (transform == null && value is Component component)
                        transform = component.transform;
                    if (transform == null && value is GameObject gameObject)
                        transform = gameObject.transform;
                    if (transform != null)
                    {
                        output.AppendLine($"  {field.Name} ({field.FieldType.Name}): " +
                            GetQaTransformPath(transform, root.transform));
                    }
                    else if (value is bool || value is int ||
                        value is float || value is string || value is Enum)
                    {
                        output.AppendLine($"  {field.Name} ({field.FieldType.Name}): " +
                            value);
                    }
                }
            }

            string[] relevant = transforms
                .Where(transform =>
                {
                    string value = (transform.name ?? "")
                        .ToLowerInvariant();
                    return value.Contains("hand") ||
                        value.Contains("weapon") ||
                        value.Contains("shield") ||
                        value.Contains("sword") ||
                        value.Contains("dagger") ||
                        value.Contains("bow") ||
                        value.Contains("hips") ||
                        value.Contains("arm");
                })
                .Select(transform =>
                    GetQaTransformPath(transform, root.transform))
                .Distinct()
                .OrderBy(value => value)
                .ToArray();
            output.AppendLine("Rig/equipment transforms (" +
                relevant.Length + "):");
            foreach (string value in relevant)
                output.AppendLine("  " + value);
        }

        private static void DumpNecromancerPetRigs()
        {
            string directory = Path.Combine(RuntimePaths.DataDirectory,
                "TestReports", "NecromancerPetRigs");
            Directory.CreateDirectory(directory);
            string path = Path.Combine(directory,
                $"{DateTime.UtcNow:yyyyMMdd-HHmmss}-" +
                SafeReportName(SkillsSaveManager.CharName) + ".txt");
            var output = new StringBuilder();
            output.AppendLine("Erenshor Necromancer pet rig audit");
            output.AppendLine("Character: " + SkillsSaveManager.CharName);

            Spell[] spells = GameData.SpellDatabase?.SpellDatabase ??
                Array.Empty<Spell>();
            var candidates = spells
                .Where(spell => spell != null &&
                    spell.Type == Spell.SpellType.Pet &&
                    spell.PetToSummon != null)
                .GroupBy(spell => spell.PetToSummon.GetInstanceID())
                .OrderBy(group => group.First().PetToSummon.name)
                .ToArray();
            output.AppendLine("Unique native pet prefabs: " +
                candidates.Length);
            foreach (IGrouping<int, Spell> group in candidates)
            {
                Spell first = group.First();
                AppendPetRig(output, first.PetToSummon,
                    string.Join(", ", group.Select(spell =>
                        spell.SpellName).Distinct().OrderBy(name => name)));
            }

            NPC[] sceneSkeletons = UnityEngine.Object.FindObjectsOfType<NPC>()
                .Where(npc =>
                {
                    string name = (npc?.NPCName ?? npc?.name ?? "")
                        .ToLowerInvariant();
                    return name.Contains("skeleton") ||
                        name.Contains("bone") ||
                        name.Contains("fallen");
                })
                .OrderBy(npc => npc.NPCName ?? npc.name)
                .ToArray();
            output.AppendLine();
            output.AppendLine("Loaded scene skeleton-like NPCs: " +
                sceneSkeletons.Length);
            foreach (NPC npc in sceneSkeletons)
                AppendPetRig(output, npc.gameObject,
                    "scene NPC " + (npc.NPCName ?? npc.name));

            SafeFileIO.TryWriteAllTextAtomic(path, output.ToString(), out _);
            SkillsPlugin.Log.LogInfo("Necromancer pet rig audit written: " +
                path);
            ChatHelper.Send(
                "<color=#4CAF50>[Test Mode]</color> Pet rig audit written.");
            ChatHelper.Send("<color=#AAAAAA>Evidence: " + path + "</color>");
        }

        private static void AppendPetRig(StringBuilder output,
            GameObject root, string sources)
        {
            if (root == null) return;
            Transform[] transforms =
                root.GetComponentsInChildren<Transform>(true);
            Animator[] animators =
                root.GetComponentsInChildren<Animator>(true);
            ModularParts[] modulars =
                root.GetComponentsInChildren<ModularParts>(true);
            Inventory[] inventories =
                root.GetComponentsInChildren<Inventory>(true);
            output.AppendLine();
            output.AppendLine("Prefab: " + root.name);
            output.AppendLine("Sources: " + sources);
            output.AppendLine($"Transforms={transforms.Length}; " +
                $"animators={animators.Length}; modularParts={modulars.Length}; " +
                $"inventories={inventories.Length}");
            foreach (Animator animator in animators)
            {
                output.AppendLine("Animator: " +
                    GetQaTransformPath(animator.transform, root.transform) +
                    $"; human={animator.isHuman}; avatar=" +
                    (animator.avatar != null ? animator.avatar.name : "<none>"));
                if (!animator.isHuman) continue;
                Transform right =
                    animator.GetBoneTransform(HumanBodyBones.RightHand);
                Transform left =
                    animator.GetBoneTransform(HumanBodyBones.LeftHand);
                output.AppendLine("  Humanoid RightHand: " +
                    GetQaTransformPath(right, root.transform));
                output.AppendLine("  Humanoid LeftHand: " +
                    GetQaTransformPath(left, root.transform));
            }
            string[] relevant = transforms
                .Where(transform =>
                {
                    string value = (transform.name ?? "").ToLowerInvariant();
                    return value.Contains("hand") ||
                        value.Contains("weapon") ||
                        value.Contains("shield") ||
                        value.Contains("sword") ||
                        value.Contains("dagger") ||
                        value.Contains("bow");
                })
                .Select(transform =>
                    GetQaTransformPath(transform, root.transform))
                .Distinct()
                .OrderBy(value => value)
                .ToArray();
            output.AppendLine("Hand/equipment transforms (" +
                relevant.Length + "):");
            foreach (string value in relevant)
                output.AppendLine("  " + value);
        }

        private static string GetQaTransformPath(Transform transform,
            Transform root)
        {
            if (transform == null) return "<none>";
            var names = new List<string>();
            Transform current = transform;
            while (current != null)
            {
                names.Add(current.name);
                if (current == root) break;
                current = current.parent;
            }
            names.Reverse();
            return string.Join("/", names);
        }

        private IEnumerator CaptureNecromancerIconCatalog()
        {
            _necromancerIconCaptureRunning = true;
            string directory = Path.Combine(RuntimePaths.DataDirectory,
                "TestReports", "NecromancerIcons",
                $"{DateTime.UtcNow:yyyyMMdd-HHmmss}-" +
                SafeReportName(SkillsSaveManager.CharName));
            Directory.CreateDirectory(directory);
            var manifest = new StringBuilder();
            manifest.AppendLine("capture\tresult\tdetail");
            GameObject overlay = null;
            int passed = 0;
            int failed = 0;
            try
            {
                GameData.PlayerInv?.ForceCloseInv();
                GameData.ItemInfoWindow?.CloseItemWindow();

                bool catalogValid = Necromancer.NecromancerCatalog
                    .ValidateCatalog(out string catalogDetail);
                if (catalogValid) passed++; else failed++;
                manifest.Append("runtime_unique_icons\t")
                    .Append(catalogValid ? "PASS" : "FAIL").Append('\t')
                    .AppendLine(Tsv(catalogDetail));

                var spellEntries = new List<KeyValuePair<string, Sprite>>
                {
                    new KeyValuePair<string, Sprite>("Necromancer Class",
                        Necromancer.NecromancerCatalog.ClassIcon)
                };
                spellEntries.AddRange(Necromancer.NecromancerCatalog.AllSpells
                    .Where(spell => spell != null)
                    .OrderBy(spell => spell.RequiredLevel)
                    .ThenBy(spell => spell.SpellName,
                        StringComparer.OrdinalIgnoreCase)
                    .Select(spell => new KeyValuePair<string, Sprite>(
                        spell.SpellName, spell.SpellIcon)));
                overlay = CreateIconAuditOverlay(
                    "Necromancer Class & Spell Icons", spellEntries, 8);
                yield return null;
                yield return new WaitForEndOfFrame();
                string spellsPath = Path.Combine(directory,
                    "spell_and_class_icons.png");
                bool spellsRequested = RequestScreenshot(spellsPath,
                    out string spellsError);
                int spellWait = 0;
                while (spellsRequested && !ScreenshotExists(spellsPath) &&
                    spellWait++ < 120) yield return null;
                bool spellsCaptured = spellsRequested &&
                    ScreenshotExists(spellsPath);
                if (spellsCaptured) passed++; else failed++;
                manifest.Append("spell_and_class_icons.png\t")
                    .Append(spellsCaptured ? "PASS" : "FAIL").Append('\t')
                    .AppendLine(Tsv(spellsError ?? ""));
                if (overlay != null) Destroy(overlay);
                overlay = null;
                yield return null;

                List<KeyValuePair<string, Sprite>> scrollEntries =
                    Necromancer.NecromancerCatalog.AllScrolls
                    .Where(scroll => scroll != null)
                    .OrderBy(scroll => scroll.TeachSpell?.RequiredLevel ?? 0)
                    .ThenBy(scroll => scroll.ItemName,
                        StringComparer.OrdinalIgnoreCase)
                    .Select(scroll => new KeyValuePair<string, Sprite>(
                        scroll.ItemName, scroll.ItemIcon)).ToList();
                overlay = CreateIconAuditOverlay(
                    "Necromancer Spell Scroll Icons", scrollEntries, 7);
                yield return null;
                yield return new WaitForEndOfFrame();
                string scrollsPath = Path.Combine(directory,
                    "spell_scroll_icons.png");
                bool scrollsRequested = RequestScreenshot(scrollsPath,
                    out string scrollsError);
                int scrollWait = 0;
                while (scrollsRequested && !ScreenshotExists(scrollsPath) &&
                    scrollWait++ < 120) yield return null;
                bool scrollsCaptured = scrollsRequested &&
                    ScreenshotExists(scrollsPath);
                if (scrollsCaptured) passed++; else failed++;
                manifest.Append("spell_scroll_icons.png\t")
                    .Append(scrollsCaptured ? "PASS" : "FAIL").Append('\t')
                    .AppendLine(Tsv(scrollsError ?? ""));
                if (overlay != null) Destroy(overlay);
                overlay = null;
                yield return null;

                var auraEntries = new List<KeyValuePair<string, Sprite>>();
                foreach (Item item in Necromancer.NecromancerAuras.AllItems
                    .Where(item => item != null)
                    .OrderBy(item => item.ItemLevel))
                {
                    auraEntries.Add(new KeyValuePair<string, Sprite>(
                        item.ItemName + " — Gem", item.ItemIcon));
                    auraEntries.Add(new KeyValuePair<string, Sprite>(
                        item.Aura.SpellName + " — Effect",
                        item.Aura.SpellIcon));
                }
                overlay = CreateIconAuditOverlay(
                    "Necromancer Aura Gems & Active Effects",
                    auraEntries, 5);
                yield return null;
                yield return new WaitForEndOfFrame();
                string aurasPath = Path.Combine(directory,
                    "aura_gem_and_effect_icons.png");
                bool aurasRequested = RequestScreenshot(aurasPath,
                    out string aurasError);
                int auraWait = 0;
                while (aurasRequested && !ScreenshotExists(aurasPath) &&
                    auraWait++ < 120) yield return null;
                bool aurasCaptured = aurasRequested &&
                    ScreenshotExists(aurasPath);
                if (aurasCaptured) passed++; else failed++;
                manifest.Append("aura_gem_and_effect_icons.png\t")
                    .Append(aurasCaptured ? "PASS" : "FAIL").Append('\t')
                    .AppendLine(Tsv(aurasError ?? ""));
                if (overlay != null) Destroy(overlay);
                overlay = null;
                yield return null;

                List<KeyValuePair<string, Sprite>> gearEntries =
                    Necromancer.NecromancerGear.AllItems
                    .Where(item => item != null)
                    .OrderBy(item => item.ItemLevel)
                    .ThenBy(item => item.ItemName,
                        StringComparer.OrdinalIgnoreCase)
                    .Select(item => new KeyValuePair<string, Sprite>(
                        item.ItemName, item.ItemIcon)).ToList();
                overlay = CreateIconAuditOverlay(
                    "Necromancer Class Gear & Charm Mold Icons",
                    gearEntries, 5);
                yield return null;
                yield return new WaitForEndOfFrame();
                string gearPath = Path.Combine(directory,
                    "class_gear_and_mold_icons.png");
                bool gearRequested = RequestScreenshot(gearPath,
                    out string gearError);
                int gearWait = 0;
                while (gearRequested && !ScreenshotExists(gearPath) &&
                    gearWait++ < 120) yield return null;
                bool gearCaptured = gearRequested &&
                    ScreenshotExists(gearPath);
                if (gearCaptured) passed++; else failed++;
                manifest.Append("class_gear_and_mold_icons.png\t")
                    .Append(gearCaptured ? "PASS" : "FAIL").Append('\t')
                    .AppendLine(Tsv(gearError ?? ""));

                SafeFileIO.TryWriteAllTextAtomic(Path.Combine(directory,
                    "manifest.tsv"), manifest.ToString(), out _);
                _lastReportPath = directory;
                ChatHelper.Send(
                    $"<color={(failed == 0 ? "#4CAF50" : "#EF5350")}>" +
                    $"[Test Mode] Necromancer icon visual audit " +
                    $"{(failed == 0 ? "PASS" : "FAIL")}: " +
                    $"{passed} checks passed, {failed} failed.</color>");
                ChatHelper.Send("<color=#AAAAAA>Evidence: " + directory +
                    "</color>");
            }
            finally
            {
                if (overlay != null) Destroy(overlay);
                _necromancerIconCaptureRunning = false;
            }
        }

        private IEnumerator RunNecromancerAuraTest()
        {
            _necromancerAuraTestRunning = true;
            string directory = Path.Combine(RuntimePaths.DataDirectory,
                "TestReports", "NecromancerAuras",
                $"{DateTime.UtcNow:yyyyMMdd-HHmmss}-" +
                SafeReportName(SkillsSaveManager.CharName));
            Directory.CreateDirectory(directory);
            var manifest = new StringBuilder();
            manifest.AppendLine("check\tresult\tdetail");
            int passed = 0;
            int failed = 0;
            try
            {
                bool catalog = Necromancer.NecromancerAuras.ValidateCatalog(
                    out string catalogDetail);
                if (catalog) passed++; else failed++;
                manifest.Append("catalog\t")
                    .Append(catalog ? "PASS" : "FAIL").Append('\t')
                    .AppendLine(Tsv(catalogDetail));

                Necromancer.NecromancerAuras.InjectCurrentSceneVendors();
                bool merchantValid = Necromancer.NecromancerAuras
                    .ValidateStarterMerchant(out string merchantDetail);
                if (merchantValid) passed++; else failed++;
                manifest.Append("starter_merchant_stock\t")
                    .Append(merchantValid ? "PASS" : "FAIL").Append('\t')
                    .AppendLine(Tsv(merchantDetail));

                VendorInventory merchant = UnityEngine.Object
                    .FindObjectsOfType<VendorInventory>()
                    .FirstOrDefault(Necromancer.NecromancerAuras
                        .IsStarterAuraMerchant);
                Item starter = Necromancer.NecromancerAuras.StarterItem;
                bool purchase = false;
                string purchaseDetail;
                if (merchant == null || starter == null ||
                    GameData.VendorWindow == null)
                {
                    purchaseDetail =
                        "starter merchant, gem, or native vendor UI unavailable";
                }
                else
                {
                    CharacterController controller = GameData.PlayerControl?
                        .GetComponent<CharacterController>();
                    if (controller != null) controller.enabled = false;
                    GameData.PlayerControl.transform.position =
                        merchant.transform.position -
                        merchant.transform.forward * 2f;
                    if (controller != null) controller.enabled = true;

                    GameData.VendorWindow.Page = 0;
                    GameData.VendorWindow.LoadWindow(
                        merchant.ItemsForSale, merchant);
                    yield return null;
                    yield return new WaitForEndOfFrame();
                    string vendorPath = Path.Combine(directory,
                        "starter_aura_merchant.png");
                    bool vendorRequested = RequestScreenshot(vendorPath,
                        out string vendorError);
                    int vendorWait = 0;
                    while (vendorRequested && !ScreenshotExists(vendorPath) &&
                        vendorWait++ < 120) yield return null;
                    bool vendorCaptured = vendorRequested &&
                        ScreenshotExists(vendorPath);
                    if (vendorCaptured) passed++; else failed++;
                    manifest.Append("starter_aura_merchant_visual\t")
                        .Append(vendorCaptured ? "PASS" : "FAIL").Append('\t')
                        .AppendLine(Tsv(vendorError ?? ""));

                    List<ItemIcon> storage = GameData.PlayerInv.StoredSlots
                        ?.Where(slot => slot != null).ToList() ??
                        new List<ItemIcon>();
                    var existing = new HashSet<ItemIcon>(storage.Where(slot =>
                        ReferenceEquals(slot.MyItem, starter)));
                    ItemIcon vendorSlot = GameData.VendorWindow.VendorSlots
                        ?.FirstOrDefault(slot => slot != null &&
                            ReferenceEquals(slot.MyItem, starter));
                    bool hasCapacity = storage.Any(slot =>
                        slot.MyItem == null || ReferenceEquals(slot.MyItem,
                            GameData.PlayerInv.Empty));
                    int oldGold = GameData.PlayerInv.Gold;
                    if (vendorSlot != null && hasCapacity)
                    {
                        GameData.PlayerInv.Gold = Mathf.Max(oldGold,
                            starter.ItemValue);
                        if (GameData.PlayerInv.GoldTXT != null)
                            GameData.PlayerInv.GoldTXT.text =
                                GameData.PlayerInv.Gold.ToString();
                        GameData.SlotActiveForVendor = vendorSlot;
                        GameData.CurSellVal = starter.ItemValue;
                        GameData.VendorWindow.Transaction();
                        yield return null;
                        ItemIcon purchasedSlot = storage.FirstOrDefault(slot =>
                            ReferenceEquals(slot.MyItem, starter) &&
                            !existing.Contains(slot));
                        purchase = purchasedSlot != null;
                        purchaseDetail = purchase
                            ? "native VendorWindow.Transaction purchased Grave's Whisper into an empty inventory slot"
                            : "native transaction did not add exactly one new starter aura";
                        if (purchasedSlot != null)
                            GameData.PlayerInv.RemoveItemFromInv(purchasedSlot);
                    }
                    else
                    {
                        purchaseDetail = vendorSlot == null
                            ? "starter gem was not visible on vendor page one"
                            : "QA inventory has no empty slot for purchase";
                    }
                    GameData.PlayerInv.Gold = oldGold;
                    if (GameData.PlayerInv.GoldTXT != null)
                        GameData.PlayerInv.GoldTXT.text = oldGold.ToString();
                    GameData.DeactivateSlotForVendor();
                    GameData.SlotActiveForVendor = null;
                    if (GameData.VendorWindow.WindowParent != null)
                        GameData.VendorWindow.WindowParent.SetActive(false);
                    GameData.VendorWindowOpen = false;
                }
                if (purchase) passed++; else failed++;
                manifest.Append("native_purchase\t")
                    .Append(purchase ? "PASS" : "FAIL").Append('\t')
                    .AppendLine(Tsv(purchaseDetail));

                bool equipment = Necromancer.NecromancerAuras
                    .ExerciseEquipmentAndSave(out string equipmentDetail);
                if (equipment) passed++; else failed++;
                manifest.Append("equip_status_save\t")
                    .Append(equipment ? "PASS" : "FAIL").Append('\t')
                    .AppendLine(Tsv(equipmentDetail));

                GameData.PlayerInv.ForceOpenInv();
                yield return null;
                yield return new WaitForEndOfFrame();
                string equippedPath = Path.Combine(directory,
                    "equipped_level35_aura.png");
                bool equippedRequested = RequestScreenshot(equippedPath,
                    out string equippedError);
                int equippedWait = 0;
                while (equippedRequested &&
                    !ScreenshotExists(equippedPath) &&
                    equippedWait++ < 120) yield return null;
                bool equippedCaptured = equippedRequested &&
                    ScreenshotExists(equippedPath);
                if (equippedCaptured) passed++; else failed++;
                manifest.Append("equipped_aura_visual\t")
                    .Append(equippedCaptured ? "PASS" : "FAIL").Append('\t')
                    .AppendLine(Tsv(equippedError ?? ""));

                SafeFileIO.TryWriteAllTextAtomic(Path.Combine(directory,
                    "manifest.tsv"), manifest.ToString(), out _);
                _lastReportPath = directory;
                ChatHelper.Send(
                    $"<color={(failed == 0 ? "#4CAF50" : "#EF5350")}>" +
                    $"[Test Mode] Necromancer aura smoke " +
                    $"{(failed == 0 ? "PASS" : "FAIL")}: " +
                    $"{passed} checks passed, {failed} failed.</color>");
                ChatHelper.Send("<color=#AAAAAA>Evidence: " + directory +
                    "</color>");
            }
            finally
            {
                _necromancerAuraTestRunning = false;
            }
        }

        private IEnumerator RunNecromancerGearTest()
        {
            _necromancerGearTestRunning = true;
            string runId = $"{DateTime.UtcNow:yyyyMMdd-HHmmss}-" +
                SafeReportName(SkillsSaveManager.CharName);
            string directory = Path.Combine(RuntimePaths.DataDirectory,
                "TestReports", "NecromancerGear", runId);
            string visualDirectory = Path.Combine(directory,
                "equipped-visuals");
            Directory.CreateDirectory(directory);
            var report = new SmokeReport();
            try
            {
                bool catalog = Necromancer.NecromancerGear.ValidateCatalog(
                    out string catalogDetail);
                report.Check("NECROMANCER GEAR", "catalog and budgets",
                    catalog, catalogDetail);

                bool acquisition = Necromancer.NecromancerGear
                    .RunAcquisitionSelfTest(out string acquisitionDetail);
                report.Check("NECROMANCER GEAR",
                    "native themed loot and world-drop mold injection",
                    acquisition, acquisitionDetail);

                foreach (Item item in Necromancer.NecromancerGear.AllItems
                    .Where(value => value != null)
                    .OrderBy(value => value.ItemLevel)
                    .ThenBy(value => value.ItemName,
                        StringComparer.OrdinalIgnoreCase))
                {
                    bool inventory = Items.BagSystem.ProbeInventoryRoundTrip(
                        item, out string inventoryError);
                    bool bag = Items.BagSystem.ProbeBagRoundTrip(
                        item, out string bagError);
                    report.Check("NECROMANCER GEAR", item.ItemName,
                        inventory && bag,
                        inventory && bag
                            ? "exact inventory and bag serialization round-trip"
                            : $"inventory={inventory}: {inventoryError}; " +
                              $"bag={bag}: {bagError}");
                    yield return null;
                }

                bool forged = ProbeNecromancerCharmForge(
                    out string forgeDetail);
                report.Check("NECROMANCER GEAR",
                    "native Smithing.Combine charm recipe", forged,
                    forgeDetail);

                List<CatalogEntry> visualGear =
                    Necromancer.NecromancerGear.EquippableItems
                    .Where(item => item != null)
                    .Select(item => new CatalogEntry
                    {
                        Name = item.ItemName,
                        Item = item,
                        Source = "necromancer-class-gear"
                    }).ToList();
                visualGear.Sort((left, right) =>
                {
                    int slot = left.Item.RequiredSlot.CompareTo(
                        right.Item.RequiredSlot);
                    return slot != 0 ? slot : string.Compare(left.Name,
                        right.Name, StringComparison.OrdinalIgnoreCase);
                });
                IEnumerator visuals = RunGearVisualChecks(report, visualGear,
                    visualDirectory);
                while (visuals.MoveNext()) yield return visuals.Current;

                bool equipped = Necromancer.NecromancerGear
                    .ExerciseEquipmentAndSave(out string equipmentDetail);
                report.Check("NECROMANCER GEAR",
                    "simultaneous equip and exact native save", equipped,
                    equipmentDetail);

                if (equipped)
                {
                    GameData.PlayerInv.ForceOpenInv();
                    GameData.HidePlayerHelm = 0;
                    if (GameData.PlayerInv.CharCam != null)
                        GameData.PlayerInv.CharCam.SetActive(true);
                    _gearVisualRunning = true;
                    _gearVisualLabel =
                        "Necromancer Class Gear — Native Inventory & Model\n" +
                        "Cowl q2 | Shroud q3 | Ossuary Scepter q11 | Charm q3";
                    int settleFrames = GetVisualSettleFrameCount();
                    for (int frame = 0; frame < settleFrames; frame++)
                        yield return null;
                    yield return new WaitForEndOfFrame();
                    string screenshotPath = Path.Combine(directory,
                        "full_saved_loadout.png");
                    bool requested = RequestScreenshot(screenshotPath,
                        out string screenshotError);
                    int wait = 0;
                    while (requested && !ScreenshotExists(screenshotPath) &&
                        wait++ < 120) yield return null;
                    bool captured = requested &&
                        ScreenshotExists(screenshotPath);
                    report.Check("NECROMANCER GEAR",
                        "full saved loadout visual", captured,
                        captured ? "full_saved_loadout.png" : screenshotError);
                }

                string reportPath = Path.Combine(directory,
                    "gear-smoke-report.txt");
                string content = report.Finish(
                    Necromancer.NecromancerGear.AllItems.Count, 1);
                if (!SafeFileIO.TryWriteAllTextAtomic(reportPath, content,
                    out string writeError))
                    throw new IOException("could not write gear report: " +
                        writeError);
                _lastReportPath = reportPath;
                ChatHelper.Send(
                    $"<color={(report.Failed == 0 ? "#4CAF50" : "#EF5350")}>" +
                    $"[Test Mode] Necromancer class gear smoke " +
                    $"{(report.Failed == 0 ? "PASS" : "FAIL")}: " +
                    $"{report.Passed} checks passed, " +
                    $"{report.Failed} failed.</color>");
                ChatHelper.Send("<color=#AAAAAA>Evidence: " + directory +
                    "</color>");
                if (report.Failed == 0)
                    ChatHelper.Send(
                        "<color=#FFD54F>[Test Mode]</color> Restart Erenshor, then run /cstest necro gear verify to prove exact native reload of all four slots and qualities.");
            }
            finally
            {
                _gearVisualRunning = false;
                _gearVisualLabel = null;
                _necromancerGearTestRunning = false;
            }
        }

        private static bool ProbeNecromancerCharmForge(out string detail)
        {
            Smithing smithing = GameData.Smithing;
            Item mold = Necromancer.NecromancerGear.MoldItem;
            Item reward = Necromancer.NecromancerGear.CharmItem;
            Item empty = GameData.PlayerInv?.Empty;
            Item fuel = GameData.ItemDB?.ItemDB?.FirstOrDefault(item =>
                item != null && item.FuelSource);
            if (smithing?.Template == null || smithing.FuelSource == null ||
                smithing.Components == null || smithing.Components.Count < 4 ||
                mold == null || reward == null || empty == null || fuel == null)
            {
                detail = "native forge slots, fuel, mold, or reward unavailable";
                return false;
            }

            Item templateItem = smithing.Template.MyItem;
            int templateQuantity = smithing.Template.Quantity;
            Item fuelItem = smithing.FuelSource.MyItem;
            int fuelQuantity = smithing.FuelSource.Quantity;
            var componentItems = smithing.Components.Select(slot =>
                slot?.MyItem).ToList();
            var componentQuantities = smithing.Components.Select(slot =>
                slot?.Quantity ?? 1).ToList();
            bool valid = false;
            string failure = null;
            try
            {
                smithing.Template.MyItem = mold;
                smithing.Template.Quantity = 1;
                smithing.FuelSource.MyItem = fuel;
                smithing.FuelSource.Quantity = 1;
                foreach (ItemIcon component in smithing.Components)
                {
                    if (component == null) continue;
                    component.MyItem = empty;
                    component.Quantity = 1;
                    component.UpdateSlotImage();
                }
                for (int index = 0;
                    index < mold.TemplateIngredients.Count; index++)
                {
                    smithing.Components[index].MyItem =
                        mold.TemplateIngredients[index];
                    smithing.Components[index].Quantity = 1;
                    smithing.Components[index].UpdateSlotImage();
                }
                smithing.Template.UpdateSlotImage();
                smithing.FuelSource.UpdateSlotImage();
                smithing.Combine();

                int rewards = smithing.Components.Count(component =>
                    component != null &&
                    ReferenceEquals(component.MyItem, reward));
                int nonEmpty = smithing.Components.Count(component =>
                    component != null && component.MyItem != null &&
                    !ReferenceEquals(component.MyItem, empty));
                valid = rewards == 1 && nonEmpty == 1 &&
                    ReferenceEquals(smithing.Components[0].MyItem, reward) &&
                    smithing.Components[0].Quantity == 1 &&
                    ReferenceEquals(smithing.Template.MyItem, empty) &&
                    ReferenceEquals(smithing.FuelSource.MyItem, empty);
                if (!valid)
                    failure = $"rewardCopies={rewards}; nonEmptyOutputs=" +
                        $"{nonEmpty}; outputQ=" +
                        $"{smithing.Components[0].Quantity}; templateEmpty=" +
                        $"{ReferenceEquals(smithing.Template.MyItem, empty)}; " +
                        $"fuelEmpty=" +
                        $"{ReferenceEquals(smithing.FuelSource.MyItem, empty)}";
            }
            catch (Exception ex)
            {
                failure = ex.Message;
            }
            finally
            {
                smithing.Template.MyItem = templateItem ?? empty;
                smithing.Template.Quantity = templateQuantity;
                smithing.Template.UpdateSlotImage();
                smithing.FuelSource.MyItem = fuelItem ?? empty;
                smithing.FuelSource.Quantity = fuelQuantity;
                smithing.FuelSource.UpdateSlotImage();
                for (int index = 0; index < smithing.Components.Count;
                    index++)
                {
                    ItemIcon component = smithing.Components[index];
                    if (component == null) continue;
                    component.MyItem = componentItems[index] ?? empty;
                    component.Quantity = componentQuantities[index];
                    component.UpdateSlotImage();
                }
            }
            detail = valid
                ? "native Smithing.Combine consumed one mold, one fuel, and " +
                  "the exact four Arcanist-parity ingredients; produced " +
                  "exactly one q1 Necromancer's Charm; forge state restored"
                : failure ?? "native combine did not produce the exact reward";
            return valid;
        }

        private static GameObject CreateIconAuditOverlay(string title,
            IList<KeyValuePair<string, Sprite>> entries, int columns)
        {
            var root = new GameObject("NecromancerIconAudit",
                typeof(RectTransform), typeof(Canvas),
                typeof(UnityEngine.UI.CanvasScaler),
                typeof(UnityEngine.UI.GraphicRaycaster));
            Canvas canvas = root.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = short.MaxValue;
            UnityEngine.UI.CanvasScaler scaler =
                root.GetComponent<UnityEngine.UI.CanvasScaler>();
            // The QA machine uses a 2560x1080 ultrawide viewport. A 1920x1080
            // ScaleWithScreenSize canvas produces a 935px logical height and
            // silently clips the first row. Lay out in actual screen pixels so
            // every icon remains inside the captured frame at any aspect ratio.
            scaler.uiScaleMode = UnityEngine.UI.CanvasScaler.ScaleMode
                .ConstantPixelSize;
            scaler.scaleFactor = 1f;

            GameObject background = CreateAuditRect("Background",
                root.transform, Vector2.zero, Vector2.one,
                Vector2.zero, Vector2.zero);
            UnityEngine.UI.Image backgroundImage =
                background.AddComponent<UnityEngine.UI.Image>();
            backgroundImage.color = new Color(0.025f, 0.035f, 0.05f, 0.985f);
            backgroundImage.raycastTarget = false;

            GameObject heading = CreateAuditRect("Heading", root.transform,
                new Vector2(0.02f, 0.925f), new Vector2(0.98f, 0.99f),
                Vector2.zero, Vector2.zero);
            TMPro.TextMeshProUGUI headingText =
                heading.AddComponent<TMPro.TextMeshProUGUI>();
            ConfigureAuditText(headingText, title, 30f,
                TMPro.TextAlignmentOptions.Center,
                new Color(0.87f, 0.9f, 0.96f, 1f));

            int rows = Mathf.CeilToInt(entries.Count /
                (float)Mathf.Max(1, columns));
            float gridLeft = 35f;
            float gridRight = 35f;
            float gridTop = 95f;
            float gridBottom = 24f;
            float canvasWidth = Mathf.Max(640f, Screen.width);
            float canvasHeight = Mathf.Max(480f, Screen.height);
            float availableWidth = canvasWidth - gridLeft - gridRight;
            float availableHeight = canvasHeight - gridTop - gridBottom;
            float cellWidth = availableWidth / columns;
            float cellHeight = availableHeight / Mathf.Max(1, rows);
            for (int index = 0; index < entries.Count; index++)
            {
                int row = index / columns;
                int column = index % columns;
                float x = gridLeft + column * cellWidth;
                float y = gridBottom + (rows - row - 1) * cellHeight;
                GameObject cell = CreateAuditRect("IconCell_" + index,
                    root.transform, Vector2.zero, Vector2.zero,
                    new Vector2(x + 4f, y + 4f),
                    new Vector2(x + cellWidth - 4f,
                        y + cellHeight - 4f));
                UnityEngine.UI.Image cellBackground =
                    cell.AddComponent<UnityEngine.UI.Image>();
                cellBackground.color = new Color(0.08f, 0.1f, 0.14f, 0.98f);
                cellBackground.raycastTarget = false;

                float iconSize = Mathf.Min(112f, cellHeight - 42f);
                GameObject iconObject = CreateAuditRect("Icon",
                    cell.transform, new Vector2(0.5f, 1f),
                    new Vector2(0.5f, 1f),
                    new Vector2(-iconSize * 0.5f,
                        -iconSize - 8f),
                    new Vector2(iconSize * 0.5f, -8f));
                UnityEngine.UI.Image iconImage =
                    iconObject.AddComponent<UnityEngine.UI.Image>();
                iconImage.sprite = entries[index].Value;
                iconImage.preserveAspect = true;
                iconImage.color = Color.white;
                iconImage.raycastTarget = false;

                GameObject labelObject = CreateAuditRect("Label",
                    cell.transform, new Vector2(0f, 0f),
                    new Vector2(1f, 0f), new Vector2(5f, 3f),
                    new Vector2(-5f, Mathf.Min(42f, cellHeight * 0.3f)));
                TMPro.TextMeshProUGUI label =
                    labelObject.AddComponent<TMPro.TextMeshProUGUI>();
                ConfigureAuditText(label, entries[index].Key, 15f,
                    TMPro.TextAlignmentOptions.Center,
                    new Color(0.86f, 0.88f, 0.92f, 1f));
                label.enableAutoSizing = true;
                label.fontSizeMin = 9f;
                label.fontSizeMax = 15f;
            }
            return root;
        }

        private static GameObject CreateAuditRect(string name,
            Transform parent, Vector2 anchorMin, Vector2 anchorMax,
            Vector2 offsetMin, Vector2 offsetMax)
        {
            var result = new GameObject(name, typeof(RectTransform),
                typeof(CanvasRenderer));
            RectTransform rect = result.GetComponent<RectTransform>();
            rect.SetParent(parent, false);
            rect.anchorMin = anchorMin;
            rect.anchorMax = anchorMax;
            rect.offsetMin = offsetMin;
            rect.offsetMax = offsetMax;
            return result;
        }

        private static void ConfigureAuditText(TMPro.TextMeshProUGUI text,
            string value, float size, TMPro.TextAlignmentOptions alignment,
            Color color)
        {
            text.text = value ?? "";
            text.font = TMPro.TMP_Settings.defaultFontAsset;
            text.fontSize = size;
            text.alignment = alignment;
            text.color = color;
            text.enableWordWrapping = true;
            text.raycastTarget = false;
        }

        internal static bool TryCaptureMonkVendor(out string detail)
        {
            if (_instance == null)
            {
                detail = "test harness is unavailable";
                return false;
            }
            if (_instance._necromancerVendorCaptureRunning)
            {
                detail = "a class-trainer capture is already running";
                return false;
            }
            _instance.StartCoroutine(
                _instance.CaptureNecromancerVendor(monk: true));
            detail = "Monk and Reaver trainer capture started";
            return true;
        }

        private IEnumerator CaptureNecromancerVendor(bool monk = false)
        {
            _necromancerVendorCaptureRunning = true;
            Necromancer.NecromancerVendorMarker necromancerMarker =
                monk ? null : UnityEngine.Object
                .FindObjectOfType<Necromancer.NecromancerVendorMarker>();
            Monk.MonkVendorMarker monkMarker = monk ? UnityEngine.Object
                .FindObjectOfType<Monk.MonkVendorMarker>() : null;
            Component marker = monk ? (Component)monkMarker :
                necromancerMarker;
            Vector3 sourcePosition = monk
                ? monkMarker?.SourcePosition ?? Vector3.zero
                : necromancerMarker?.SourcePosition ?? Vector3.zero;
            string className = monk ? "Monk" : "Necromancer";
            VendorInventory vendor = marker != null
                ? marker.GetComponent<VendorInventory>() : null;
            Character vendorCharacter = marker != null
                ? marker.GetComponent<Character>() : null;
            PlayerControl player = GameData.PlayerControl;
            Character priorTarget = player?.CurrentTarget;
            string scene = UnityEngine.SceneManagement.SceneManager
                .GetActiveScene().name;
            string directory = Path.Combine(RuntimePaths.DataDirectory,
                "TestReports", monk ? "MonkTrainers" :
                    "NecromancerVendors",
                $"{DateTime.UtcNow:yyyyMMdd-HHmmss}-" +
                SafeReportName(scene));
            Directory.CreateDirectory(directory);
            string locationPath = Path.Combine(directory,
                SafeFileComponent(scene) + "_trainer_location.png");
            string storePath = Path.Combine(directory,
                SafeFileComponent(scene) + "_vendor_store.png");
            bool locationExists = false;
            bool storeExists = false;
            string locationError = "";
            string storeError = "";
            bool valid = false;
            string validDetail = "vendor was not ready";
            string framingDetail = "capture camera was not configured";
            GearCaptureCameraHook cameraHook = null;
            Renderer[] playerRenderers = Array.Empty<Renderer>();
            bool[] playerRendererStates = Array.Empty<bool>();
            try
            {
                if (marker == null || vendor == null ||
                    vendorCharacter == null || player == null ||
                    GameData.VendorWindow == null ||
                    player.Myself == null || !player.Myself.Alive)
                {
                    ChatHelper.Send(
                        $"<color=#EF5350>[Test Mode]</color> A live player and the current scene's {className} trainer are required for capture.");
                    yield break;
                }

                playerRenderers = GetQaPlayerRenderers(null);
                playerRendererStates = playerRenderers
                    .Select(renderer => renderer != null && renderer.enabled)
                    .ToArray();
                foreach (Renderer renderer in playerRenderers)
                    if (renderer != null) renderer.enabled = false;

                GameData.PlayerInv?.ForceCloseInv();
                GameData.ItemInfoWindow?.CloseItemWindow();
                if (GameData.VendorWindow?.WindowParent?.activeSelf == true)
                    GameData.VendorWindow.CloseWindow();
                if (priorTarget != null && priorTarget != vendorCharacter)
                    priorTarget.UntargetMe();

                Vector3 cameraPosition = ChooseVendorCaptureCameraPosition(
                    marker.transform, sourcePosition,
                    out Vector3 cameraFocus, out framingDetail);
                Vector3 cameraGroundDirection = cameraPosition -
                    marker.transform.position;
                cameraGroundDirection.y = 0f;
                if (cameraGroundDirection.sqrMagnitude < 0.01f)
                    cameraGroundDirection = Vector3.back;
                cameraGroundDirection.Normalize();
                Vector3 sideDirection = Vector3.Cross(Vector3.up,
                    cameraGroundDirection).normalized;
                Vector3 requested = marker.transform.position -
                    cameraGroundDirection * 5f + sideDirection * 4f;
                Vector3 destination = GameData.GetSafeNavMeshPoint(requested);
                CharacterController controller = player
                    .GetComponent<CharacterController>();
                bool controllerEnabled = controller != null &&
                    controller.enabled;
                if (controller != null) controller.enabled = false;
                player.transform.position = destination;
                Vector3 look = marker.transform.position -
                    player.transform.position;
                look.y = 0f;
                if (look.sqrMagnitude > 0.01f)
                    player.transform.rotation = Quaternion.LookRotation(look,
                        Vector3.up);
                if (controller != null) controller.enabled = controllerEnabled;
                player.CurrentTarget = vendorCharacter;
                vendorCharacter.TargetMe();

                Camera captureCamera = GameData.CamControl?.ActualCam ??
                    Camera.main ?? player.camera;
                if (captureCamera == null)
                {
                    locationError = "active game camera was unavailable";
                    ChatHelper.Send(
                        "<color=#EF5350>[Test Mode]</color> The active game camera is unavailable for vendor capture.");
                    yield break;
                }
                cameraHook = captureCamera.gameObject
                    .AddComponent<GearCaptureCameraHook>();
                cameraHook.ForceFront = false;
                cameraHook.PosePosition = cameraPosition;
                cameraHook.PoseRotation = Quaternion.LookRotation(
                    cameraFocus - cameraPosition, Vector3.up);
                cameraHook.ForcePose = true;

                for (int frame = 0; frame < 30; frame++) yield return null;
                valid = monk
                    ? Monk.MonkVendors.ValidateCurrentSceneVendor(
                        out validDetail)
                    : Necromancer.NecromancerVendors
                        .ValidateCurrentSceneVendor(out validDetail);
                bool locationRequested = RequestScreenshot(locationPath,
                    out locationError);
                int wait = 0;
                while (locationRequested &&
                    !ScreenshotExists(locationPath) && wait++ < 120)
                    yield return null;
                locationExists = locationRequested &&
                    ScreenshotExists(locationPath);

                GameData.VendorWindow.Page = 0;
                GameData.VendorWindow.LoadWindow(vendor.ItemsForSale, vendor);
                yield return new WaitForSecondsRealtime(0.25f);
                yield return new WaitForEndOfFrame();
                bool storeRequested = RequestScreenshot(storePath,
                    out storeError);
                wait = 0;
                while (storeRequested && !ScreenshotExists(storePath) &&
                    wait++ < 120) yield return null;
                storeExists = storeRequested && ScreenshotExists(storePath);

                var report = new StringBuilder();
                report.AppendLine("Scene: " + scene);
                report.AppendLine("Placement: " + (monk
                    ? Monk.MonkVendors.DescribeCurrentScenePlacement()
                    : Necromancer.NecromancerVendors
                        .DescribeCurrentScenePlacement()));
                report.AppendLine("Validation: " +
                    (valid ? "PASS" : "FAIL") + " - " + validDetail);
                report.AppendLine("Camera framing: " + framingDetail);
                report.AppendLine("Hidden local-player renderers: " +
                    playerRenderers.Length);
                report.AppendLine("Location screenshot: " +
                    (locationExists ? "PASS" : "FAIL") + " - " +
                    locationPath + " - " + (locationError ?? ""));
                report.AppendLine("Store screenshot: " +
                    (storeExists ? "PASS" : "FAIL") + " - " +
                    storePath + " - " + (storeError ?? ""));
                if (!monk)
                {
                    report.AppendLine("Native vendor candidates:");
                    foreach (string candidate in Necromancer
                        .NecromancerVendors.DescribeNativeVendors())
                        report.AppendLine("- " + candidate);
                }
                SafeFileIO.TryWriteAllTextAtomic(Path.Combine(directory,
                    "report.txt"), report.ToString(), out _);
                _lastReportPath = directory;
                bool passed = valid && locationExists && storeExists;
                ChatHelper.Send(
                    $"<color={(passed ? "#4CAF50" : "#EF5350")}>" +
                    $"[Test Mode] {scene} {className} trainer visual " +
                    $"{(passed ? "PASS" : "FAIL")}: {validDetail}.</color>");
                ChatHelper.Send("<color=#AAAAAA>Evidence: " + directory +
                    "</color>");
            }
            finally
            {
                for (int index = 0; index < playerRenderers.Length; index++)
                    if (playerRenderers[index] != null)
                        playerRenderers[index].enabled =
                            playerRendererStates[index];
                if (cameraHook != null)
                {
                    cameraHook.ForcePose = false;
                    Destroy(cameraHook);
                }
                if (GameData.VendorWindow?.WindowParent?.activeSelf == true)
                    GameData.VendorWindow.CloseWindow();
                if (player != null && vendorCharacter != null &&
                    player.CurrentTarget == vendorCharacter)
                {
                    vendorCharacter.UntargetMe();
                    player.CurrentTarget = priorTarget;
                    if (priorTarget != null) priorTarget.TargetMe();
                }
                _necromancerVendorCaptureRunning = false;
            }
        }

        private static Vector3 ChooseVendorCaptureCameraPosition(
            Transform marker, Vector3 markerSourcePosition,
            out Vector3 focus, out string detail)
        {
            Transform source = UnityEngine.Object
                .FindObjectsOfType<VendorInventory>()
                .Where(value => value != null &&
                    value.GetComponent<Necromancer.NecromancerVendorMarker>() ==
                        null)
                .OrderBy(value => Vector3.Distance(value.transform.position,
                    markerSourcePosition))
                .Select(value => value.transform)
                .FirstOrDefault();
            Vector3 sourcePosition = source != null
                ? source.position : markerSourcePosition;
            float groundHeight = Mathf.Max(marker.position.y,
                sourcePosition.y);
            focus = (marker.position + sourcePosition) * 0.5f;
            focus.y = groundHeight + 1.25f;

            Vector3 best = focus + Vector3.back * 6.5f +
                Vector3.up * 1.4f;
            float bestScore = float.MinValue;
            bool bestVendorClear = false;
            bool bestSourceClear = false;
            float bestRadius = 0f;
            float bestHeight = 0f;
            float[] radii = { 6.5f, 8f, 10f };
            float[] heights = { 1.7f, 2.4f, 3.3f };
            for (int radiusIndex = 0; radiusIndex < radii.Length;
                radiusIndex++)
            {
                float radius = radii[radiusIndex];
                for (int heightIndex = 0; heightIndex < heights.Length;
                    heightIndex++)
                {
                    float height = heights[heightIndex];
                    for (int directionIndex = 0; directionIndex < 24;
                        directionIndex++)
                    {
                        float radians = directionIndex * Mathf.PI * 2f / 24f;
                        Vector3 direction = new Vector3(Mathf.Sin(radians),
                            0f, Mathf.Cos(radians));
                        Vector3 candidate = focus + direction * radius;
                        candidate.y = groundHeight + height;
                        bool occupied = Physics.CheckSphere(candidate, 0.35f,
                            Physics.DefaultRaycastLayers,
                            QueryTriggerInteraction.Ignore);
                        bool vendorClear = HasClearCaptureView(candidate,
                            marker);
                        bool sourceClear = source == null ||
                            HasClearCaptureView(candidate, source);
                        float score = vendorClear ? 1800f : -1800f;
                        score += sourceClear ? 1600f : -800f;
                        if (!occupied) score += 500f;
                        score -= radiusIndex * 25f + heightIndex * 10f;
                        if (score <= bestScore) continue;
                        bestScore = score;
                        best = candidate;
                        bestVendorClear = vendorClear;
                        bestSourceClear = sourceClear;
                        bestRadius = radius;
                        bestHeight = height;
                    }
                }
            }
            detail = $"camera={best}; focus={focus}; radius={bestRadius:0.0}m; " +
                $"height={bestHeight:0.0}m; vendorLineOfSight=" +
                $"{(bestVendorClear ? "clear" : "blocked")}; sourceLineOfSight=" +
                $"{(bestSourceClear ? "clear" : "blocked")}";
            return best;
        }

        private static bool HasClearCaptureView(Vector3 from,
            Transform target)
        {
            if (target == null) return true;
            Vector3 to = target.position + Vector3.up * 1.2f;
            Vector3 direction = to - from;
            if (direction.sqrMagnitude < 0.01f) return true;
            from += direction.normalized * 0.2f;
            if (!Physics.Linecast(from, to, out RaycastHit hit,
                Physics.DefaultRaycastLayers,
                QueryTriggerInteraction.Ignore)) return true;
            Transform hitTransform = hit.transform;
            return hitTransform == target ||
                (hitTransform != null &&
                    (hitTransform.IsChildOf(target) ||
                        target.IsChildOf(hitTransform)));
        }

        private IEnumerator CaptureNecromancerPetEquipmentMotion()
        {
            _necromancerPetMotionRunning = true;
            NPC pet = Necromancer.NecromancerPets.ActivePet;
            Character player = GameData.PlayerControl?.Myself;
            Character petCharacter = pet?.GetChar();
            Necromancer.NecromancerPetMarker marker =
                pet?.GetComponent<Necromancer.NecromancerPetMarker>();
            if (pet == null || player == null || petCharacter == null ||
                marker == null)
            {
                ChatHelper.Send(
                    "<color=#EF5350>[Test Mode]</color> Summon a Bone Thrall before running the weapon audit.");
                _necromancerPetMotionRunning = false;
                yield break;
            }

            string originalGear =
                Necromancer.NecromancerPetEquipment.Serialize();
            Character previousPlayerTarget =
                GameData.PlayerControl.CurrentTarget;
            Character previousPetTarget = pet.CurrentAggroTarget;
            Vector3 originalPetPosition = pet.transform.position;
            Quaternion originalPetRotation = pet.transform.rotation;
            bool originalMobile = pet.Mobile;
            bool originalPetEnabled = pet.enabled;
            UnityEngine.AI.NavMeshAgent petAgent =
                pet.GetComponent<UnityEngine.AI.NavMeshAgent>();
            bool petAgentEnabled = petAgent != null && petAgent.enabled;
            bool originalPetStopped = petAgentEnabled &&
                petAgent.isOnNavMesh && petAgent.isStopped;
            GameObject dummyObject = null;
            Character dummyCharacter = null;
            string directory = Path.Combine(RuntimePaths.DataDirectory,
                "TestReports", "NecromancerPetWeapons",
                $"{DateTime.UtcNow:yyyyMMdd-HHmmss}-" +
                SafeReportName(SkillsSaveManager.CharName));
            Directory.CreateDirectory(directory);
            string reportPath = Path.Combine(directory,
                "pet-weapon-audit.txt");
            _lastReportPath = reportPath;
            var report = new StringBuilder();
            var results = new List<bool>();
            try
            {
                ChatHelper.Send(
                    "<color=#FFD54F>[Test Mode]</color> Bone Thrall weapon audit started: bow projectile, one-hand plus shield, and two-hand native attack paths.");
                Item bow = SelectNecromancerPetAuditItem(
                    "Traveler's Bow", item => item.IsBow ||
                        item.ThisWeaponType == Item.WeaponType.TwoHandBow);
                Item oneHand = SelectNecromancerPetAuditItem(
                    "Copper Sword", item =>
                        item.ThisWeaponType ==
                            Item.WeaponType.OneHandMelee ||
                        item.ThisWeaponType ==
                            Item.WeaponType.OneHandDagger);
                Item shield = SelectNecromancerPetAuditItem(
                    "Bark Shield", item => item.Shield);
                Item twoHand = SelectNecromancerPetAuditItem(
                    null, item =>
                        item.ThisWeaponType ==
                            Item.WeaponType.TwoHandMelee ||
                        item.ThisWeaponType ==
                            Item.WeaponType.TwoHandStaff);
                if (bow == null || oneHand == null || shield == null ||
                    twoHand == null)
                {
                    report.AppendLine("Result: FAIL");
                    report.AppendLine("Required item resolution failed: " +
                        $"bow={bow?.ItemName ?? "<missing>"}, " +
                        $"one-hand={oneHand?.ItemName ?? "<missing>"}, " +
                        $"shield={shield?.ItemName ?? "<missing>"}, " +
                        $"two-hand={twoHand?.ItemName ?? "<missing>"}");
                    yield break;
                }

                GameObject prefab =
                    Resources.Load<GameObject>("NPCs/TestDummy");
                if (prefab == null)
                {
                    report.AppendLine("Result: FAIL");
                    report.AppendLine(
                        "Native training-dummy prefab is unavailable.");
                    yield break;
                }
                Vector3 requested =
                    GameData.PlayerControl.transform.position +
                    GameData.PlayerControl.transform.forward * 3f;
                Vector3 dummyPosition =
                    GameData.GetSafeNavMeshPoint(requested);
                dummyObject = Instantiate(prefab, dummyPosition,
                    GameData.PlayerControl.transform.rotation);
                dummyObject.name = "CS Necromancer Pet Weapon Dummy";
                dummyCharacter = dummyObject.GetComponent<Character>();
                NPC dummyNpc = dummyObject.GetComponent<NPC>();
                Stats dummyStats = dummyObject.GetComponent<Stats>();
                if (dummyCharacter == null || dummyNpc == null ||
                    dummyStats == null)
                {
                    report.AppendLine("Result: FAIL");
                    report.AppendLine(
                        "Native training dummy lacks combat components.");
                    yield break;
                }
                dummyCharacter.MyFaction = Character.Faction.DEBUG;
                dummyCharacter.BaseFaction = Character.Faction.DEBUG;
                dummyCharacter.TempFaction = Character.Faction.DEBUG;
                dummyCharacter.Invulnerable = true;
                yield return null;
                yield return null;
                dummyNpc.enabled = false;
                UnityEngine.AI.NavMeshAgent dummyAgent =
                    dummyObject.GetComponent<UnityEngine.AI.NavMeshAgent>();
                if (dummyAgent != null) dummyAgent.enabled = false;
                dummyNpc.NPCName = "Bone Thrall Weapon Dummy";
                dummyNpc.NeverAggro = false;
                dummyNpc.SummonedByPlayer = false;
                dummyNpc.SimPlayer = false;
                dummyNpc.AggroTable = new List<AggroSlot>();
                dummyCharacter.Alive = true;
                dummyCharacter.Master = null;
                dummyCharacter.DPSDummy = true;
                dummyCharacter.MyFaction = Character.Faction.OtherEvil;
                dummyCharacter.BaseFaction = Character.Faction.OtherEvil;
                dummyCharacter.TempFaction = Character.Faction.OtherEvil;
                dummyCharacter.Invulnerable = false;
                dummyStats.PlayerOrSimPlayer = false;
                dummyStats.Level = Mathf.Max(1, player.MyStats.Level);
                dummyStats.BaseHP = 500000000;
                dummyStats.OverrideHPforNPC = true;
                dummyStats.StopAllRegen = true;
                dummyStats.CalcStats();
                dummyStats.StopAllRegen = true;
                dummyStats.CurrentHP = dummyStats.CurrentMaxHP;

                Vector3 petPosition = GameData.GetSafeNavMeshPoint(
                    dummyPosition -
                    GameData.PlayerControl.transform.forward * 1.5f);
                if (petAgentEnabled) petAgent.Warp(petPosition);
                else pet.transform.position = petPosition;
                pet.transform.LookAt(new Vector3(dummyPosition.x,
                    pet.transform.position.y, dummyPosition.z));
                pet.Mobile = false;
                pet.enabled = false;
                if (petAgentEnabled && petAgent.isOnNavMesh)
                    petAgent.isStopped = true;
                pet.CurrentAggroTarget = dummyCharacter;
                GameData.PlayerControl.CurrentTarget = dummyCharacter;

                MethodInfo performHit = typeof(NPC).GetMethod(
                    "PerformMeleeHit",
                    BindingFlags.Instance | BindingFlags.NonPublic);
                if (performHit == null)
                {
                    report.AppendLine("Result: FAIL");
                    report.AppendLine(
                        "NPC.PerformMeleeHit could not be resolved.");
                    yield break;
                }

                report.AppendLine("Necromancer Bone Thrall Weapon Audit");
                report.AppendLine("Character: " + player.MyStats.MyName);
                report.AppendLine("Pet tier: " + marker.Tier);
                report.AppendLine("Original gear: " +
                    Necromancer.NecromancerPetEquipment.Describe());
                report.AppendLine();

                var loadouts = new[]
                {
                    new { Label = "bow", Primary = bow,
                        Secondary = (Item)null },
                    new { Label = "one-hand + shield", Primary = oneHand,
                        Secondary = shield },
                    new { Label = "two-hand", Primary = twoHand,
                        Secondary = (Item)null }
                };
                foreach (var loadout in loadouts)
                {
                    bool applied = Necromancer.NecromancerPetEquipment
                        .ApplyTransientTestLoadout(loadout.Primary, 1,
                            loadout.Secondary, 1,
                            out string validationDetail);
                    float requestedDistance =
                        loadout.Label == "bow" ? 10f : 1.5f;
                    Vector3 loadoutPetPosition =
                        GameData.GetSafeNavMeshPoint(dummyPosition -
                            GameData.PlayerControl.transform.forward *
                            requestedDistance);
                    if (petAgentEnabled)
                        petAgent.Warp(loadoutPetPosition);
                    else
                        pet.transform.position = loadoutPetPosition;
                    pet.transform.LookAt(new Vector3(dummyPosition.x,
                        pet.transform.position.y, dummyPosition.z));
                    yield return null;
                    yield return null;
                    Inventory inventory = pet.GetComponent<Inventory>();
                    Animator animator = pet.GetComponent<Animator>();
                    float targetDistance = Vector3.Distance(
                        pet.transform.position, dummyPosition);
                    int healthBefore = dummyStats.CurrentHP;
                    Necromancer.NecromancerPets.ResetWeaponAuditCounters();
                    int attempts = loadout.Label == "bow" ? 5 : 8;
                    string invocationError = null;
                    for (int attempt = 0; attempt < attempts; attempt++)
                    {
                        try
                        {
                            performHit.Invoke(pet,
                                new object[] { pet.BaseAtkDmg, false });
                        }
                        catch (TargetInvocationException exception)
                        {
                            invocationError =
                                (exception.InnerException ?? exception)
                                    .Message;
                            break;
                        }
                        yield return null;
                    }
                    if (loadout.Label == "bow")
                        yield return new WaitForSeconds(2.5f);
                    Necromancer.NecromancerBowAnimationDriver bowAnimation =
                        pet.GetComponent<
                            Necromancer.NecromancerBowAnimationDriver>();
                    int damage = Mathf.Max(0,
                        healthBefore - dummyStats.CurrentHP);
                    bool bowMode = loadout.Label == "bow" &&
                        inventory != null && inventory.PrimaryBow &&
                        inventory.TwoHandPrimary &&
                        Necromancer.NecromancerPets
                            .BowAttackAttemptCount >= attempts &&
                        Necromancer.NecromancerPets
                            .BowProjectileLaunchCount >= attempts &&
                        Necromancer.NecromancerPets
                            .BowProjectileDeliveryCount > 0 &&
                        petCharacter.AttackRange >= 14f &&
                        targetDistance <= petCharacter.AttackRange &&
                        bowAnimation != null && bowAnimation.RigReady &&
                        bowAnimation.PlayCount >= attempts &&
                        damage > 0;
                    bool oneHandMode =
                        loadout.Label == "one-hand + shield" &&
                        inventory != null && inventory.PrimaryWeapon &&
                        !inventory.PrimaryBow &&
                        !inventory.TwoHandPrimary &&
                        inventory.SecondaryShield &&
                        pet.OneHand && !pet.TwoHandSword &&
                        !pet.TwoHandStaff &&
                        AnimatorBoolEquals(animator, "1HSmall", true) &&
                        Necromancer.NecromancerPets
                            .MeleeAttackAttemptCount >= attempts &&
                        damage > 0;
                    bool expectsStaff =
                        loadout.Primary.ThisWeaponType ==
                            Item.WeaponType.TwoHandStaff;
                    bool twoHandMode = loadout.Label == "two-hand" &&
                        inventory != null && inventory.PrimaryWeapon &&
                        !inventory.PrimaryBow &&
                        inventory.TwoHandPrimary &&
                        (expectsStaff ? pet.TwoHandStaff :
                            pet.TwoHandSword) &&
                        AnimatorBoolEquals(animator,
                            expectsStaff ? "2HStaff" : "2HMelee", true) &&
                        Necromancer.NecromancerPets
                            .MeleeAttackAttemptCount >= attempts &&
                        damage > 0;
                    bool passed = applied &&
                        string.IsNullOrEmpty(invocationError) &&
                        (bowMode || oneHandMode || twoHandMode);
                    results.Add(passed);
                    report.AppendLine(
                        $"{loadout.Label}: {(passed ? "PASS" : "FAIL")}");
                    report.AppendLine("  items: " +
                        loadout.Primary.ItemName +
                        (loadout.Secondary != null
                            ? " + " + loadout.Secondary.ItemName : ""));
                    report.AppendLine("  types: " +
                        loadout.Primary.ThisWeaponType +
                        (loadout.Secondary != null
                            ? " + shield=" + loadout.Secondary.Shield : ""));
                    report.AppendLine($"  applied={applied}; " +
                        $"damage={damage}; invocation=" +
                        $"{invocationError ?? "ok"}");
                    report.AppendLine("  runtime: " +
                        $"primaryWeapon={inventory?.PrimaryWeapon}; " +
                        $"primaryBow={inventory?.PrimaryBow}; " +
                        $"twoHandPrimary={inventory?.TwoHandPrimary}; " +
                        $"secondaryShield={inventory?.SecondaryShield}; " +
                        $"attackRange={petCharacter.AttackRange:0.0}; " +
                        $"targetDistance={targetDistance:0.0}; " +
                        $"oneHand={pet.OneHand}; " +
                        $"twoHandSword={pet.TwoHandSword}; " +
                        $"twoHandStaff={pet.TwoHandStaff}");
                    report.AppendLine("  attacks: " +
                        $"melee={Necromancer.NecromancerPets.MeleeAttackAttemptCount}; " +
                        $"bow-attempts={Necromancer.NecromancerPets.BowAttackAttemptCount}; " +
                        $"bow-launched={Necromancer.NecromancerPets.BowProjectileLaunchCount}; " +
                        $"bow-delivered={Necromancer.NecromancerPets.BowProjectileDeliveryCount}; " +
                        $"bow-recorded-damage={Necromancer.NecromancerPets.BowDamageDelivered}; " +
                        $"bow-animation-ready={bowAnimation?.RigReady}; " +
                        $"bow-animation-plays={bowAnimation?.PlayCount ?? 0}");
                    report.AppendLine("  validation: " +
                        validationDetail);
                    report.AppendLine();
                    dummyStats.CurrentHP = dummyStats.CurrentMaxHP;
                }

                bool allPassed = results.Count == 3 &&
                    results.All(value => value);
                report.Insert(0,
                    $"Result: {(allPassed ? "PASS" : "FAIL")}\n");
                ChatHelper.Send(
                    $"<color={(allPassed ? "#4CAF50" : "#EF5350")}>" +
                    $"[Test Mode] Bone Thrall weapon audit " +
                    $"{(allPassed ? "PASS" : "FAIL")}: bow=" +
                    $"{(results.Count > 0 && results[0])}; " +
                    $"one-hand+shield=" +
                    $"{(results.Count > 1 && results[1])}; two-hand=" +
                    $"{(results.Count > 2 && results[2])}.</color>");
                ChatHelper.Send("<color=#AAAAAA>Evidence: " +
                    reportPath + "</color>");
            }
            finally
            {
                SafeFileIO.TryWriteAllTextAtomic(reportPath,
                    report.ToString(), out _);
                Necromancer.NecromancerPetEquipment
                    .RestoreSerializedInMemory(originalGear);
                Necromancer.NecromancerPets.ApplyEscrowToActivePet();
                pet.CurrentAggroTarget = previousPetTarget;
                pet.Mobile = originalMobile;
                pet.enabled = originalPetEnabled;
                if (petAgentEnabled) petAgent.Warp(originalPetPosition);
                else pet.transform.position = originalPetPosition;
                pet.transform.rotation = originalPetRotation;
                if (petAgentEnabled && petAgent.isOnNavMesh)
                    petAgent.isStopped = originalPetStopped;
                if (GameData.PlayerControl.CurrentTarget == dummyCharacter)
                    GameData.PlayerControl.CurrentTarget = null;
                if (previousPlayerTarget != null)
                {
                    previousPlayerTarget.TargetMe();
                    GameData.PlayerControl.CurrentTarget =
                        previousPlayerTarget;
                }
                if (dummyObject != null) Destroy(dummyObject);
                _necromancerPetMotionRunning = false;
            }
        }

        private static Item SelectNecromancerPetAuditItem(string preferred,
            Func<Item, bool> predicate)
        {
            List<Item> candidates = BuildCatalog(true)
                .Select(entry => entry.Item)
                .Where(item => item != null && predicate(item) &&
                    (item.WeaponDmg > 0 || item.Shield))
                .Where(item =>
                    !string.IsNullOrWhiteSpace(item.EquipmentToActivate))
                .Distinct()
                .ToList();
            Item exact = candidates.FirstOrDefault(item =>
                !string.IsNullOrEmpty(preferred) &&
                string.Equals(item.ItemName, preferred,
                    StringComparison.OrdinalIgnoreCase));
            return exact ?? candidates
                .OrderBy(item => item.ItemLevel)
                .ThenBy(item => item.ItemName,
                    StringComparer.OrdinalIgnoreCase)
                .FirstOrDefault();
        }

        private static bool AnimatorBoolEquals(Animator animator,
            string name, bool expected)
        {
            if (animator == null) return false;
            AnimatorControllerParameter parameter = animator.parameters
                .FirstOrDefault(value =>
                    value.type ==
                        AnimatorControllerParameterType.Bool &&
                    string.Equals(value.name, name,
                        StringComparison.Ordinal));
            return parameter != null &&
                animator.GetBool(parameter.name) == expected;
        }

        private IEnumerator CaptureNecromancerPetEquipment()
        {
            NPC pet = Necromancer.NecromancerPets.ActivePet;
            bool managedThrall = pet != null;
            if (pet == null)
                pet = GameData.PlayerControl?.Myself?.MyCharmedNPC;
            if (pet == null)
            {
                ChatHelper.Send(
                    "<color=#EF5350>[Test Mode]</color> Summon a pet before inspecting its rig.");
                yield break;
            }

            Character priorTarget = GameData.PlayerControl.CurrentTarget;
            Vector3 originalPosition = pet.transform.position;
            Quaternion originalRotation = pet.transform.rotation;
            bool originalMobile = pet.Mobile;
            bool originalPetEnabled = pet.enabled;
            UnityEngine.AI.NavMeshAgent agent =
                pet.GetComponent<UnityEngine.AI.NavMeshAgent>();
            bool agentEnabled = agent != null && agent.enabled;
            bool originalAgentRotation = agentEnabled && agent.updateRotation;
            bool originalAgentStopped = agentEnabled && agent.isOnNavMesh &&
                agent.isStopped;
            Renderer[] playerRenderers = GetQaPlayerRenderers(pet);
            bool[] rendererStates = playerRenderers
                .Select(renderer => renderer != null && renderer.enabled)
                .ToArray();
            string directory = Path.Combine(RuntimePaths.DataDirectory,
                "TestReports", "NecromancerPetGear",
                $"{DateTime.UtcNow:yyyyMMdd-HHmmss}-" +
                SafeReportName(SkillsSaveManager.CharName));
            Directory.CreateDirectory(directory);
            try
            {
                GameData.PlayerInv?.ForceCloseInv();
                GameData.ItemInfoWindow?.CloseItemWindow();
                if (priorTarget != null) priorTarget.UntargetMe();
                GameData.PlayerControl.CurrentTarget = null;
                pet.Mobile = false;

                Vector3 requested = GameData.PlayerControl.transform.position +
                    GameData.PlayerControl.transform.forward * 2.35f;
                Vector3 destination = GameData.GetSafeNavMeshPoint(requested);
                if (agentEnabled) agent.Warp(destination);
                else pet.transform.position = destination;
                if (agentEnabled)
                {
                    agent.updateRotation = false;
                    if (agent.isOnNavMesh) agent.isStopped = true;
                }
                Vector3 towardPlayer = GameData.PlayerControl.transform.position -
                    pet.transform.position;
                towardPlayer.y = 0f;
                if (towardPlayer.sqrMagnitude > 0.01f)
                    pet.transform.rotation = Quaternion.LookRotation(towardPlayer,
                        Vector3.up);
                // NPC.Update can rotate the thrall even while Mobile is false.
                // Disable that behaviour after placement so the evidence really
                // is a stable front/back equipment inspection.
                pet.enabled = false;

                // Keep the gameplay camera and rotate only the thrall. Hiding
                // the local player's renderers prevents the third-person body
                // from obscuring the smaller skeleton and its hand equipment.
                foreach (Renderer renderer in playerRenderers)
                    if (renderer != null) renderer.enabled = false;

                for (int frame = 0; frame < 15; frame++) yield return null;
                bool valid;
                string detail;
                if (managedThrall)
                    valid = Necromancer.NecromancerPets
                        .ValidateActivePetEquipment(out detail);
                else
                {
                    valid = true;
                    detail = "native rig preview=" +
                        (pet.NPCName ?? pet.name);
                }
                string front = Path.Combine(directory, "pet_gear_front.png");
                RequestScreenshot(front, out string frontError);
                yield return new WaitForEndOfFrame();
                int wait = 0;
                while (!ScreenshotExists(front) && wait++ < 120)
                    yield return null;

                if (towardPlayer.sqrMagnitude > 0.01f)
                    pet.transform.rotation = Quaternion.LookRotation(-towardPlayer,
                        Vector3.up);
                yield return null;
                yield return new WaitForEndOfFrame();
                string back = Path.Combine(directory, "pet_gear_back.png");
                RequestScreenshot(back, out string backError);
                yield return new WaitForEndOfFrame();
                wait = 0;
                while (!ScreenshotExists(back) && wait++ < 120)
                    yield return null;

                bool screenshots = ScreenshotExists(front) &&
                    ScreenshotExists(back);
                string report =
                    $"result={(valid && screenshots ? "PASS" : "FAIL")}\n" +
                    $"mode={(managedThrall ? "managed-thrall" : "native-rig-preview")}\n" +
                    $"detail={detail}\nfront={front}\nback={back}\n" +
                    $"hidden_player_renderers={playerRenderers.Length}\n" +
                    $"remaining_near_camera_renderers=" +
                    $"{DescribeNearCameraRenderers(pet)}\n" +
                    $"front_error={frontError ?? ""}\n" +
                    $"back_error={backError ?? ""}\n";
                SafeFileIO.TryWriteAllTextAtomic(Path.Combine(directory,
                    "report.txt"), report, out _);
                ChatHelper.Send(
                    $"<color={(valid && screenshots ? "#4CAF50" : "#EF5350")}>" +
                    "[Test Mode] Pet equipment visual " +
                    $"{(valid && screenshots ? "PASS" : "FAIL")}: " +
                    $"{detail}.</color>");
                ChatHelper.Send("<color=#AAAAAA>Evidence: " + directory +
                    "</color>");
            }
            finally
            {
                for (int index = 0; index < playerRenderers.Length; index++)
                    if (playerRenderers[index] != null)
                        playerRenderers[index].enabled = rendererStates[index];
                if (agentEnabled) agent.Warp(originalPosition);
                else pet.transform.position = originalPosition;
                pet.transform.rotation = originalRotation;
                if (agentEnabled)
                {
                    agent.updateRotation = originalAgentRotation;
                    if (agent.isOnNavMesh) agent.isStopped = originalAgentStopped;
                }
                pet.enabled = originalPetEnabled;
                pet.Mobile = originalMobile;
                if (priorTarget != null)
                {
                    priorTarget.TargetMe();
                    GameData.PlayerControl.CurrentTarget = priorTarget;
                }
            }
        }

        private static Renderer[] GetQaPlayerRenderers(NPC pet)
        {
            var roots = new HashSet<Transform>();
            AddQaRendererRoot(roots, GameData.PlayerControl?.transform);
            AddQaRendererRoot(roots,
                GameData.PlayerControl?.Myself?.transform);
            AddQaRendererRoot(roots, GameData.PlayerStats?.transform);
            AddQaRendererRoot(roots, GameData.PlayerInv?.transform);
            AddQaRendererRoot(roots,
                GameData.PlayerInv?.Modulars?.transform);
            AddQaRendererRoot(roots,
                GameData.PlayerModularParent?.transform);
            return roots
                .SelectMany(root =>
                    root.GetComponentsInChildren<Renderer>(true))
                .Where(renderer => renderer != null &&
                    (pet == null ||
                        !renderer.transform.IsChildOf(pet.transform)) &&
                    renderer.GetComponentInParent<
                        Necromancer.NecromancerPetMarker>() == null)
                .Distinct()
                .ToArray();
        }

        private static void AddQaRendererRoot(HashSet<Transform> roots,
            Transform root)
        {
            if (root != null) roots.Add(root);
        }

        private static string DescribeNearCameraRenderers(NPC pet)
        {
            Camera camera = Camera.main;
            if (camera == null) return "<no main camera>";
            return string.Join(" | ", UnityEngine.Object
                .FindObjectsOfType<Renderer>()
                .Where(renderer => renderer != null && renderer.enabled &&
                    renderer.gameObject.activeInHierarchy &&
                    (pet == null ||
                        !renderer.transform.IsChildOf(pet.transform)))
                .Select(renderer => new
                {
                    Renderer = renderer,
                    Distance = Vector3.Distance(
                        camera.transform.position,
                        renderer.bounds.ClosestPoint(
                            camera.transform.position))
                })
                .Where(value => value.Distance <= 1.5f)
                .OrderBy(value => value.Distance)
                .Take(20)
                .Select(value =>
                    GetQaTransformPath(value.Renderer.transform,
                        value.Renderer.transform.root) +
                    $"@{value.Distance:F2}m size=" +
                    value.Renderer.bounds.size.ToString("F2")));
        }

        private IEnumerator RunNecromancerPetRoleTest()
        {
            _necromancerPetRoleRunning = true;
            NPC pet = Necromancer.NecromancerPets.ActivePet;
            Character player = GameData.PlayerControl?.Myself;
            Character petCharacter = pet?.GetChar();
            Necromancer.NecromancerPetMarker marker = petCharacter != null
                ? petCharacter.GetComponent<Necromancer.NecromancerPetMarker>()
                : null;
            if (player == null || pet == null || petCharacter == null ||
                marker == null)
            {
                ChatHelper.Send(
                    "<color=#EF5350>[Test Mode]</color> Summon a Bone Thrall before running the pet-role test.");
                _necromancerPetRoleRunning = false;
                yield break;
            }

            string directory = Path.Combine(RuntimePaths.DataDirectory,
                "TestReports", "NecromancerPetRole",
                $"{DateTime.UtcNow:yyyyMMdd-HHmmss}-" +
                SafeReportName(SkillsSaveManager.CharName));
            Directory.CreateDirectory(directory);
            string reportPath = Path.Combine(directory, "pet-role-report.txt");
            _lastReportPath = reportPath;
            GameObject dummyObject = null;
            Character dummyCharacter = null;
            Character previousPlayerTarget = GameData.PlayerControl.CurrentTarget;
            Character previousPetTarget = pet.CurrentAggroTarget;
            Vector3 originalPetPosition = pet.transform.position;
            Quaternion originalPetRotation = pet.transform.rotation;
            bool originalMobile = pet.Mobile;
            float originalNextTaunt = marker.NextTauntAt;
            UnityEngine.AI.NavMeshAgent petAgent =
                pet.GetComponent<UnityEngine.AI.NavMeshAgent>();
            bool petAgentEnabled = petAgent != null && petAgent.enabled;
            bool originalPetStopped = petAgentEnabled && petAgent.isOnNavMesh &&
                petAgent.isStopped;

            ChatHelper.Send(
                "<color=#FFD54F>[Test Mode]</color> Bone Thrall role test started: live melee taunt, cooldown, tank-protection, HP, armor, and damage-envelope checks.");
            try
            {
                GameObject prefab = Resources.Load<GameObject>("NPCs/TestDummy");
                if (prefab == null)
                {
                    ChatHelper.Send(
                        "<color=#EF5350>[Test Mode]</color> Native training-dummy prefab is unavailable.");
                    yield break;
                }

                Vector3 requested = GameData.PlayerControl.transform.position +
                    GameData.PlayerControl.transform.forward * 3f;
                Vector3 dummyPosition = GameData.GetSafeNavMeshPoint(requested);
                dummyObject = Instantiate(prefab, dummyPosition,
                    GameData.PlayerControl.transform.rotation);
                dummyObject.name = "CS Necromancer Pet Role Dummy";
                dummyCharacter = dummyObject.GetComponent<Character>();
                NPC dummyNpc = dummyObject.GetComponent<NPC>();
                Stats dummyStats = dummyObject.GetComponent<Stats>();
                if (dummyCharacter == null || dummyNpc == null ||
                    dummyStats == null)
                {
                    ChatHelper.Send(
                        "<color=#EF5350>[Test Mode]</color> Native training dummy lacks required combat components.");
                    yield break;
                }

                // Keep the native prefab harmless while its Start methods run.
                dummyCharacter.MyFaction = Character.Faction.DEBUG;
                dummyCharacter.BaseFaction = Character.Faction.DEBUG;
                dummyCharacter.TempFaction = Character.Faction.DEBUG;
                dummyCharacter.AggressiveTowards?.Clear();
                dummyCharacter.Invulnerable = true;
                yield return null;
                yield return null;

                dummyNpc.enabled = false;
                UnityEngine.AI.NavMeshAgent dummyAgent =
                    dummyObject.GetComponent<UnityEngine.AI.NavMeshAgent>();
                if (dummyAgent != null) dummyAgent.enabled = false;
                dummyNpc.NPCName = "Bone Thrall Role Dummy";
                dummyNpc.NeverAggro = false;
                dummyNpc.SummonedByPlayer = false;
                dummyNpc.SimPlayer = false;
                dummyNpc.BaseAtkDmg = 0;
                dummyNpc.OHAtkDmg = 0;
                dummyNpc.AggroTable = new List<AggroSlot>();
                dummyCharacter.Alive = true;
                dummyCharacter.Master = null;
                dummyCharacter.DPSDummy = true;
                dummyCharacter.MyFaction = Character.Faction.OtherEvil;
                dummyCharacter.BaseFaction = Character.Faction.OtherEvil;
                dummyCharacter.TempFaction = Character.Faction.OtherEvil;
                dummyCharacter.AggressiveTowards?.Clear();
                dummyCharacter.Invulnerable = false;
                if (GameData.PlayerControl?.Myself?.MyCharmedNPC == dummyNpc)
                    GameData.PlayerControl.Myself.MyCharmedNPC = null;
                dummyStats.PlayerOrSimPlayer = false;
                dummyStats.Level = Mathf.Max(1, player.MyStats.Level);
                dummyStats.BaseHP = 500000000;
                dummyStats.OverrideHPforNPC = true;
                dummyStats.StopAllRegen = true;
                dummyStats.CalcStats();
                dummyStats.StopAllRegen = true;
                dummyStats.CurrentHP = dummyStats.CurrentMaxHP;

                Vector3 petPosition = GameData.GetSafeNavMeshPoint(
                    dummyPosition - GameData.PlayerControl.transform.forward *
                    1.5f);
                if (petAgentEnabled) petAgent.Warp(petPosition);
                else pet.transform.position = petPosition;
                pet.transform.LookAt(new Vector3(dummyPosition.x,
                    pet.transform.position.y, dummyPosition.z));
                pet.Mobile = true;
                if (petAgentEnabled && petAgent.isOnNavMesh)
                    petAgent.isStopped = false;

                petCharacter.NearbyEnemies?.Add(dummyCharacter);
                player.NearbyEnemies?.Add(dummyCharacter);
                GameData.PlayerControl.CurrentTarget = dummyCharacter;
                marker.NextTauntAt = 0f;
                int tauntsBefore = marker.SuccessfulTaunts;
                dummyNpc.AggroTable.Add(new AggroSlot(player, 50000));
                dummyNpc.AggroTable.Add(new AggroSlot(petCharacter, 10));
                dummyNpc.CurrentAggroTarget = player;
                pet.CurrentAggroTarget = dummyCharacter;
                pet.AggroOn(dummyCharacter);

                float deadline = Time.realtimeSinceStartup + 12f;
                while (marker.SuccessfulTaunts == tauntsBefore &&
                    Time.realtimeSinceStartup < deadline)
                    yield return null;

                AggroSlot livePetSlot = dummyNpc.GetAggroSlot(petCharacter);
                bool liveTaunt = marker.SuccessfulTaunts > tauntsBefore &&
                    dummyNpc.CurrentAggroTarget == petCharacter &&
                    livePetSlot != null && livePetSlot.Hate > 50000;
                bool cooldownRejected = !Necromancer.NecromancerPets
                    .TryGraveChallenge(dummyCharacter, petCharacter, false,
                        out string cooldownDetail) &&
                    cooldownDetail.IndexOf("cooling down",
                        StringComparison.OrdinalIgnoreCase) >= 0;

                float activeCooldown = marker.NextTauntAt;
                int tauntsBeforeTankGuard = marker.SuccessfulTaunts;
                marker.NextTauntAt = 0f;
                dummyNpc.CurrentAggroTarget = dummyCharacter;
                bool stoleFromTank = Necromancer.NecromancerPets
                    .TryGraveChallenge(dummyCharacter, petCharacter, false,
                        out string tankGuardDetail);
                bool tankProtected = !stoleFromTank &&
                    dummyNpc.CurrentAggroTarget == dummyCharacter &&
                    marker.SuccessfulTaunts == tauntsBeforeTankGuard &&
                    tankGuardDetail.IndexOf("another tank",
                        StringComparison.OrdinalIgnoreCase) >= 0;
                marker.NextTauntAt = activeCooldown;

                int ascensionRank = Necromancer.NecromancerAscensions.GetRank(
                    player.MySkills,
                    Necromancer.NecromancerAscensions.BoneSovereigntyId);
                Necromancer.NecromancerPets.GetBaseProfile(marker.Tier,
                    player.MyStats.Level, ascensionRank,
                    out int expectedHp, out int expectedDamage);
                Stats petStats = petCharacter.MyStats;
                bool durableProfile = marker.BasePetHp == expectedHp &&
                    petStats.CurrentMaxHP >= expectedHp &&
                    marker.BasePetArmor > petStats.Level * 15;
                bool damageUnchanged = marker.BasePetDamage == expectedDamage;
                bool balanceCurve = Necromancer.NecromancerPets
                    .ValidateBalanceCurve(out string curveDetail);
                bool passed = liveTaunt && cooldownRejected && tankProtected &&
                    durableProfile && damageUnchanged && balanceCurve;

                var report = new StringBuilder();
                report.AppendLine("Necromancer Bone Thrall Role Test");
                report.AppendLine("Character: " + player.MyStats.MyName);
                report.AppendLine("Result: " + (passed ? "PASS" : "FAIL"));
                report.AppendLine($"Live melee taunt: {liveTaunt} " +
                    $"(taunts {tauntsBefore}->{marker.SuccessfulTaunts}, " +
                    $"pet hate {livePetSlot?.Hate ?? -1})");
                report.AppendLine($"Six-second cooldown guard: " +
                    $"{cooldownRejected} ({cooldownDetail})");
                report.AppendLine($"Other-tank protection: {tankProtected} " +
                    $"({tankGuardDetail})");
                report.AppendLine($"Durability profile: {durableProfile} " +
                    $"(tier {marker.Tier}, HP {marker.BasePetHp}/" +
                    $"{petStats.CurrentMaxHP}, expected {expectedHp}, " +
                    $"base AC {marker.BasePetArmor})");
                report.AppendLine($"Damage envelope unchanged: " +
                    $"{damageUnchanged} ({marker.BasePetDamage}/" +
                    $"{expectedDamage})");
                report.AppendLine("Curve: " + curveDetail);
                SafeFileIO.TryWriteAllTextAtomic(reportPath,
                    report.ToString(), out _);
                ChatHelper.Send(
                    $"<color={(passed ? "#4CAF50" : "#EF5350")}>" +
                    $"[Test Mode] Bone Thrall role " +
                    $"{(passed ? "PASS" : "FAIL")}: live taunt={liveTaunt}; " +
                    $"cooldown={cooldownRejected}; other tank protected=" +
                    $"{tankProtected}; durable={durableProfile}; " +
                    $"damage unchanged={damageUnchanged}.</color>");
                ChatHelper.Send("<color=#AAAAAA>Evidence: " + reportPath +
                    "</color>");
            }
            finally
            {
                marker.NextTauntAt = originalNextTaunt;
                if (dummyCharacter != null)
                {
                    petCharacter.NearbyEnemies?.Remove(dummyCharacter);
                    player.NearbyEnemies?.Remove(dummyCharacter);
                }
                pet.CurrentAggroTarget = previousPetTarget;
                pet.Mobile = originalMobile;
                if (petAgentEnabled) petAgent.Warp(originalPetPosition);
                else pet.transform.position = originalPetPosition;
                pet.transform.rotation = originalPetRotation;
                if (petAgentEnabled && petAgent.isOnNavMesh)
                    petAgent.isStopped = originalPetStopped;
                if (GameData.PlayerControl.CurrentTarget == dummyCharacter)
                    GameData.PlayerControl.CurrentTarget = null;
                if (previousPlayerTarget != null)
                {
                    previousPlayerTarget.TargetMe();
                    GameData.PlayerControl.CurrentTarget = previousPlayerTarget;
                }
                if (dummyObject != null) Destroy(dummyObject);
                _necromancerPetRoleRunning = false;
            }
        }

        private static Spell ResolveNecromancerSpell(string query)
        {
            query = (query ?? "").Trim();
            List<Spell> matches = Necromancer.NecromancerCatalog
                .GetLearnableSpells().Where(spell =>
                    spell.SpellName.IndexOf(query,
                        StringComparison.OrdinalIgnoreCase) >= 0).ToList();
            Spell exact = matches.FirstOrDefault(spell =>
                string.Equals(spell.SpellName, query,
                    StringComparison.OrdinalIgnoreCase));
            if (exact != null) return exact;
            if (matches.Count == 1) return matches[0];
            ChatHelper.Send(matches.Count == 0
                ? $"<color=#EF5350>[Test Mode]</color> No Necromancer spell matches '{query}'."
                : $"<color=#EF5350>[Test Mode]</color> '{query}' matches " +
                  string.Join(", ", matches.Take(8).Select(value => value.SpellName)) + ".");
            return null;
        }

        private static VisualCaptureState BeginNecromancerVisualCapture()
        {
            var state = new VisualCaptureState();
            Character player = GameData.PlayerControl?.Myself;
            state.Player = player;
            if (player != null)
            {
                state.PlayerHp = player.MyStats?.CurrentHP ?? 0;
                state.PlayerInvulnerable = player.Invulnerable;
                player.Alive = true;
                // Native self buffs and pet summons abort before resolution
                // when their player target is invulnerable. The isolated QA
                // target has its AI disabled, so keeping the revived player
                // damageable is both safe and faithful to a real cast.
                player.Invulnerable = false;
                if (player.MyStats != null)
                    player.MyStats.CurrentHP = Mathf.Max(1,
                        player.MyStats.CurrentMaxHP);
                Animator animator = player.GetComponent<Animator>();
                if (animator != null)
                {
                    animator.SetBool("Dead", false);
                    animator.SetTrigger("Revive");
                }
                PlayerCombat combat = player.GetComponent<PlayerCombat>();
                if (combat != null) combat.ForceAttackOff();
            }
            if (GameData.RespawnWindow != null)
                GameData.RespawnWindow.SetActive(false);

            foreach (Canvas canvas in FindObjectsOfType<Canvas>(true))
            {
                if (canvas == null || !canvas.gameObject.activeInHierarchy)
                    continue;
                state.Canvases.Add(new CanvasState
                {
                    Canvas = canvas,
                    Enabled = canvas.enabled
                });
                canvas.enabled = false;
            }

            foreach (Character character in FindObjectsOfType<Character>(true))
            {
                if (character == null ||
                    !character.gameObject.activeInHierarchy) continue;
                foreach (Renderer renderer in character
                    .GetComponentsInChildren<Renderer>(true))
                {
                    if (renderer == null) continue;
                    state.BystanderRenderers.Add(new RendererState
                    {
                        Renderer = renderer,
                        Enabled = renderer.enabled
                    });
                    renderer.enabled = false;
                }
            }

            // Existing zone actors must not wander into or fight inside the
            // deterministic cast stage while their renderers are hidden.
            // Test targets and summoned pets are created after this snapshot,
            // so their native behavior remains available to the cast pipeline.
            foreach (NPC npc in FindObjectsOfType<NPC>(true))
            {
                if (npc == null || !npc.gameObject.activeInHierarchy ||
                    npc.GetComponent<Character>() == player) continue;
                state.BystanderBehaviours.Add(new BehaviourState
                {
                    Behaviour = npc,
                    Enabled = npc.enabled
                });
                npc.enabled = false;
            }
            foreach (SimPlayer sim in FindObjectsOfType<SimPlayer>(true))
            {
                if (sim == null || !sim.gameObject.activeInHierarchy ||
                    sim.GetComponent<Character>() == player) continue;
                state.BystanderBehaviours.Add(new BehaviourState
                {
                    Behaviour = sim,
                    Enabled = sim.enabled
                });
                sim.enabled = false;
            }
            // CharmedNPC is the player's pet-command UI/input controller. A
            // synthetic QA input device can expose a transient D-pad axis and
            // invoke GoAway mid-sweep, destroying the very pet being tested.
            // Casting and the summoned NPC do not depend on this controller.
            foreach (CharmedNPC petCommands in FindObjectsOfType<CharmedNPC>(true))
            {
                if (petCommands == null || !petCommands.enabled) continue;
                state.BystanderBehaviours.Add(new BehaviourState
                {
                    Behaviour = petCommands,
                    Enabled = true
                });
                petCommands.enabled = false;
            }
            return state;
        }

        private static void EndNecromancerVisualCapture(
            VisualCaptureState state)
        {
            if (state == null) return;
            foreach (BehaviourState behaviourState in state.BystanderBehaviours)
                if (behaviourState.Behaviour != null)
                    behaviourState.Behaviour.enabled = behaviourState.Enabled;
            foreach (RendererState rendererState in state.BystanderRenderers)
                if (rendererState.Renderer != null)
                    rendererState.Renderer.enabled = rendererState.Enabled;
            foreach (CanvasState canvasState in state.Canvases)
                if (canvasState.Canvas != null)
                    canvasState.Canvas.enabled = canvasState.Enabled;

            Character player = state.Player;
            if (player != null)
            {
                player.Alive = true;
                player.Invulnerable = state.PlayerInvulnerable;
                if (player.MyStats != null)
                    player.MyStats.CurrentHP = Mathf.Max(1, state.PlayerHp);
                Animator animator = player.GetComponent<Animator>();
                if (animator != null) animator.SetBool("Dead", false);
            }
            if (GameData.RespawnWindow != null)
                GameData.RespawnWindow.SetActive(false);
        }

        private static void HideTransientCaptureCanvases(
            VisualCaptureState state)
        {
            if (state == null) return;
            foreach (Canvas canvas in FindObjectsOfType<Canvas>(true))
            {
                if (canvas == null || !canvas.gameObject.activeInHierarchy ||
                    !canvas.enabled || state.Canvases.Any(value =>
                        value.Canvas == canvas)) continue;
                state.Canvases.Add(new CanvasState
                {
                    Canvas = canvas,
                    Enabled = true
                });
                canvas.enabled = false;
            }
            foreach (TMPro.TextMeshPro worldText in
                FindObjectsOfType<TMPro.TextMeshPro>(true))
            {
                if (worldText == null || !worldText.enabled ||
                    !worldText.gameObject.activeInHierarchy) continue;
                state.BystanderBehaviours.Add(new BehaviourState
                {
                    Behaviour = worldText,
                    Enabled = true
                });
                worldText.enabled = false;
            }
        }

        private static void StageNecromancerCapturePet(NPC pet)
        {
            if (pet == null || GameData.PlayerControl == null) return;
            Transform playerTransform = GameData.PlayerControl.transform;
            Vector3 stagePosition = playerTransform.position +
                playerTransform.forward * 4.4f -
                playerTransform.right * 1.25f;
            var nav = pet.GetComponent<UnityEngine.AI.NavMeshAgent>();
            if (nav != null && nav.enabled && nav.isOnNavMesh)
            {
                nav.Warp(stagePosition);
                nav.isStopped = true;
                nav.velocity = Vector3.zero;
            }
            else
            {
                pet.transform.position = stagePosition;
            }
            pet.transform.rotation = Quaternion.LookRotation(
                -playerTransform.forward, Vector3.up);
            pet.Mobile = false;
            pet.CurrentAggroTarget = null;
            pet.AggroTable?.Clear();
        }

        private IEnumerator RunNecromancerVfxPreview(Spell selected)
        {
            _necromancerVfxRunning = true;
            VisualCaptureState captureState = null;
            string runId = $"{DateTime.UtcNow:yyyyMMdd-HHmmss}-" +
                SafeReportName(SkillsSaveManager.CharName);
            string directory = Path.Combine(RuntimePaths.DataDirectory,
                "TestReports", "NecromancerVfx", runId);
            Directory.CreateDirectory(directory);
            var manifest = new StringBuilder();
            manifest.AppendLine(
                "spell\tcharge_index\tresolve_index\tscreenshot\tresult\tdetail");
            IEnumerable<Spell> spells = selected != null
                ? new[] { selected }
                : Necromancer.NecromancerCatalog.GetLearnableSpells();
            int passed = 0;
            int failed = 0;
            Necromancer.NecromancerAudio.GetPlaybackCounts(
                out _, out int resolveAudioBefore, out int audioSourcesBefore);
            try
            {
                captureState = BeginNecromancerVisualCapture();
                yield return null;
                foreach (Spell spell in spells)
                {
                    _gearVisualLabel = "Necromancer VFX: " + spell.SpellName;
                    Vector3 offset = GameData.PlayerControl.transform.forward * 2.2f +
                        Vector3.up * 0.15f;
                    bool played = Necromancer.NecromancerVfx.PlayResolvePreview(
                        spell, GameData.PlayerControl.transform, offset,
                        out string error);
                    // Resolve bursts need a few rendered frames to develop
                    // their authored color/shape. Capturing on the spawn frame
                    // only records the initial bloom flash.
                    yield return new WaitForSecondsRealtime(0.34f);
                    HideTransientCaptureCanvases(captureState);
                    yield return new WaitForEndOfFrame();
                    string screenshot = Path.Combine(directory,
                        SafeFileComponent(spell.SpellName) + ".png");
                    string captureError = null;
                    bool captured = played && RequestScreenshot(screenshot,
                        out captureError);
                    int screenshotWaitFrames = 0;
                    while (captured && !ScreenshotExists(screenshot) &&
                        screenshotWaitFrames++ < 120)
                        yield return null;
                    bool exists = captured && ScreenshotExists(screenshot);
                    if (played && exists) passed++; else failed++;
                    manifest.Append(Tsv(spell.SpellName)).Append('\t')
                        .Append(spell.SpellChargeFXIndex).Append('\t')
                        .Append(spell.SpellResolveFXIndex).Append('\t')
                        .Append(Tsv(screenshot)).Append('\t')
                        .Append(played && exists ? "PASS" : "FAIL").Append('\t')
                        .Append(Tsv(error ?? captureError)).AppendLine();
                    Necromancer.NecromancerVfx.ClearResolvePreview();
                    yield return null;
                    yield return new WaitForSecondsRealtime(0.08f);
                }
                string manifestPath = Path.Combine(directory, "manifest.tsv");
                SafeFileIO.TryWriteAllTextAtomic(manifestPath,
                    manifest.ToString(), out _);
                Necromancer.NecromancerAudio.GetPlaybackCounts(
                    out _, out int resolveAudioAfter,
                    out int audioSourcesAfter);
                int expectedAudio = passed + failed;
                int resolveAudioPlayed =
                    resolveAudioAfter - resolveAudioBefore;
                int sourcesPlayed = audioSourcesAfter - audioSourcesBefore;
                bool audioPass = resolveAudioPlayed == expectedAudio &&
                    sourcesPlayed == expectedAudio &&
                    Necromancer.NecromancerAudio.BuiltUniqueClipCount ==
                        Necromancer.NecromancerSpellData.All.Count * 2;
                ChatHelper.Send(
                    $"<color={(failed == 0 && audioPass ? "#4CAF50" : "#EF5350")}>[Test Mode] " +
                    $"Necromancer VFX preview complete: {passed} passed, " +
                    $"{failed} failed; resolve audio={resolveAudioPlayed}/" +
                    $"{expectedAudio}, sources={sourcesPlayed}/" +
                    $"{expectedAudio}, cached unique clips=" +
                    $"{Necromancer.NecromancerAudio.BuiltUniqueClipCount}/" +
                    $"{Necromancer.NecromancerSpellData.All.Count * 2}." +
                    "</color>");
                ChatHelper.Send("<color=#AAAAAA>Evidence: " + directory + "</color>");
            }
            finally
            {
                Necromancer.NecromancerVfx.ClearResolvePreview();
                EndNecromancerVisualCapture(captureState);
                _gearVisualLabel = null;
                _necromancerVfxRunning = false;
            }
        }

        private IEnumerator RunNecromancerCastSweep()
        {
            _necromancerCastRunning = true;
            string runId = $"{DateTime.UtcNow:yyyyMMdd-HHmmss}-" +
                SafeReportName(SkillsSaveManager.CharName);
            string directory = Path.Combine(RuntimePaths.DataDirectory,
                "TestReports", "NecromancerCasts", runId);
            Directory.CreateDirectory(directory);
            var manifest = new StringBuilder();
            manifest.AppendLine(
                "spell\tstarted\tresolved\tbehavior\tscreenshot\tresult\tdetail");

            Stats player = GameData.PlayerStats;
            CastSpell caster = GameData.PlayerControl?.Myself?.MySpells;
            int originalHp = player?.CurrentHP ?? 0;
            int originalBaseHp = player?.BaseHP ?? 0;
            int originalMana = player?.CurrentMana ?? 0;
            bool originalInvulnerable = GameData.PlayerControl?.Myself?.Invulnerable ??
                false;
            VisualCaptureState captureState = null;
            GameObject dummyObject = null;
            Stats dummyStats = null;
            int passed = 0;
            int failed = 0;

            List<Spell> all = Necromancer.NecromancerCatalog
                .GetLearnableSpells().ToList();
            List<Spell> summons = all.Where(spell =>
                Necromancer.NecromancerCatalog.TryGetDefinition(spell,
                    out var definition) && definition.Kind ==
                    Necromancer.NecromancerSpellKind.SummonPet).ToList();
            Spell reclaim = all.FirstOrDefault(spell =>
                Necromancer.NecromancerCatalog.TryGetDefinition(spell,
                    out var definition) && definition.Kind ==
                    Necromancer.NecromancerSpellKind.ReclaimPet);
            var sequence = new List<Spell>();
            if (summons.Count > 0) sequence.Add(summons[0]);
            sequence.AddRange(all.Where(spell =>
                !summons.Contains(spell) && spell != reclaim));
            sequence.AddRange(summons.Skip(1));
            if (reclaim != null) sequence.Add(reclaim);

            ChatHelper.Send(
                "<color=#FFD54F>[Test Mode]</color> Real Necromancer cast sweep started. " +
                "All 35 spells will use Erenshor's charge and resolve pipeline.");
            Necromancer.NecromancerRuntime.ResetResolutionProbe();

            try
            {
                if (player == null || caster == null || sequence.Count !=
                    Necromancer.NecromancerSpellData.ExpectedLearnableSpellCount)
                    throw new InvalidOperationException(
                        "player caster or complete spell sequence is unavailable");
                captureState = BeginNecromancerVisualCapture();
                // PBAE spells legitimately include the caster in their native
                // vessel/collector path. John EverQuest's ordinary level-one
                // health pool is too small for a level-19 QA cast and can die,
                // which also makes the native charmed-pet system dismiss its
                // skeleton. A temporary health reserve keeps the real damage
                // and status pipeline intact without using invulnerability.
                player.BaseHP = Mathf.Max(player.BaseHP, 100000);
                player.CalcStats();
                player.CurrentHP = player.CurrentMaxHP;
                yield return null;

                Spell petTemplateSpell = Necromancer.NecromancerCatalog
                    .GetSpell("bone_thrall_1");
                if (petTemplateSpell?.PetToSummon == null)
                    throw new InvalidOperationException(
                        "the native skeleton test-target prefab is unavailable");
                Vector3 dummyPosition = GameData.PlayerControl.transform.position +
                    GameData.PlayerControl.transform.forward * 7f +
                    GameData.PlayerControl.transform.right * 1.5f;
                Quaternion dummyRotation = Quaternion.LookRotation(
                    -GameData.PlayerControl.transform.forward, Vector3.up);
                dummyObject = Instantiate(petTemplateSpell.PetToSummon,
                    dummyPosition, dummyRotation);
                dummyObject.name = "CS Necromancer Cast Test Target";
                Character dummyCharacter = dummyObject.GetComponent<Character>();
                NPC dummyNpc = dummyObject.GetComponent<NPC>();
                dummyStats = dummyObject.GetComponent<Stats>();
                if (dummyCharacter == null || dummyNpc == null ||
                    dummyStats == null)
                    throw new InvalidOperationException(
                        "the native skeleton test target lacks required components");
                // Keep the prefab harmless during its native Start methods.
                dummyCharacter.MyFaction = Character.Faction.DEBUG;
                dummyCharacter.BaseFaction = Character.Faction.DEBUG;
                dummyCharacter.TempFaction = Character.Faction.DEBUG;
                dummyCharacter.AggressiveTowards?.Clear();
                dummyCharacter.Invulnerable = true;
                yield return null;
                yield return null;
                dummyNpc.enabled = false;
                if (GameData.PlayerControl?.Myself?.MyCharmedNPC == dummyNpc)
                    GameData.PlayerControl.Myself.MyCharmedNPC = null;
                var dummyNav = dummyObject.GetComponent<
                    UnityEngine.AI.NavMeshAgent>();
                if (dummyNav != null) dummyNav.enabled = false;
                dummyNpc.NPCName = "Necromancer Cast Test Target";
                if (dummyNpc.NamePlateTxt != null)
                    dummyNpc.NamePlateTxt.gameObject.SetActive(false);
                foreach (Light light in dummyObject.GetComponentsInChildren<Light>(true))
                    light.enabled = false;
                foreach (ParticleSystem particles in dummyObject
                    .GetComponentsInChildren<ParticleSystem>(true))
                {
                    particles.Stop(true,
                        ParticleSystemStopBehavior.StopEmittingAndClear);
                    particles.Clear(true);
                }
                dummyNpc.SummonedByPlayer = false;
                dummyNpc.SimPlayer = false;
                dummyNpc.InGroup = false;
                dummyNpc.BaseAtkDmg = 0;
                dummyNpc.OHAtkDmg = 0;
                dummyNpc.CurrentAggroTarget = null;
                dummyNpc.AggroTable?.Clear();
                dummyCharacter.Master = null;
                dummyCharacter.MyFaction = Character.Faction.Undead;
                dummyCharacter.BaseFaction = Character.Faction.Undead;
                dummyCharacter.TempFaction = Character.Faction.Undead;
                dummyCharacter.AggressiveTowards?.Clear();
                dummyCharacter.Invulnerable = false;
                dummyStats.PlayerOrSimPlayer = false;
                dummyStats.Level = 1;
                dummyStats.BaseHP = 100000;
                dummyStats.OverrideHPforNPC = true;
                dummyStats.CalcStats();
                dummyStats.CurrentHP = dummyStats.CurrentMaxHP;
                Character playerCharacter = GameData.PlayerControl.Myself;
                if (playerCharacter.NearbyEnemies != null &&
                    !playerCharacter.NearbyEnemies.Contains(dummyCharacter))
                    playerCharacter.NearbyEnemies.Add(dummyCharacter);
                if (dummyCharacter.NearbyEnemies != null &&
                    !dummyCharacter.NearbyEnemies.Contains(playerCharacter))
                    dummyCharacter.NearbyEnemies.Add(playerCharacter);
                List<Character> meleeTargets = GameData.PlayerCombat?
                    .MyMeleeRange?.GetNPCsInRange();
                if (meleeTargets != null &&
                    !meleeTargets.Contains(dummyCharacter))
                    meleeTargets.Add(dummyCharacter);

                for (int index = 0; index < sequence.Count; index++)
                {
                    Spell spell = sequence[index];
                    Necromancer.NecromancerCatalog.TryGetDefinition(spell,
                        out var definition);
                    bool started = false;
                    bool resolved = false;
                    bool behavior = false;
                    bool screenshotExists = false;
                    string detail = "";
                    string behaviorDetail = "";
                    Stats target = null;
                    SimPlayer temporarySim = null;
                    NPC temporarySimNpc = null;
                    bool simWasInGroup = false;
                    bool simNpcWasInGroup = false;
                    Stats soulTransfusionTarget = null;
                    bool soulTargetWasPlayerOrSim = false;
                    int soulTargetMana = 0;
                    float originalCastTime = spell.SpellChargeTime;
                    float originalRange = spell.SpellRange;
                    float originalResist = spell.ResistModifier;
                    int targetHpBefore = 0;
                    int playerHpBefore = 0;
                    int playerManaBefore = 0;
                    int simManaBefore = 0;

                    try
                    {
                        player.Myself.Alive = true;
                        player.Myself.Invulnerable = false;
                        player.CurrentHP = player.CurrentMaxHP;
                        NPC currentPet = Necromancer.NecromancerPets.ActivePet;
                        if (currentPet != null)
                        {
                            Stats currentPetStats = currentPet.GetComponent<Stats>();
                            Character currentPetCharacter =
                                currentPet.GetComponent<Character>();
                            currentPet.Mobile = false;
                            currentPet.CurrentAggroTarget = null;
                            currentPet.AggroTable?.Clear();
                            if (currentPetCharacter != null)
                                currentPetCharacter.Alive = true;
                            if (currentPetStats != null)
                                currentPetStats.CurrentHP =
                                    currentPetStats.CurrentMaxHP;
                            StageNecromancerCapturePet(currentPet);
                        }
                        if (definition.Kind ==
                            Necromancer.NecromancerSpellKind.SummonPet)
                        {
                            NPC occupied = GameData.PlayerControl?.Myself?
                                .MyCharmedNPC;
                            if (occupied != null && occupied.GetComponent<
                                    Necromancer.NecromancerPetMarker>() == null)
                            {
                                SkillsPlugin.Log.LogInfo(
                                    "[Necro Cast QA] Clearing non-Necromancer " +
                                    $"companion contamination '{occupied.name}' " +
                                    "before summon.");
                                GameData.PlayerControl.Myself.MyCharmedNPC = null;
                            }
                        }
                        if (definition.Kind ==
                            Necromancer.NecromancerSpellKind.SummonPet &&
                            Necromancer.NecromancerPets.ActivePet != null)
                        {
                            NPC oldPet = Necromancer.NecromancerPets.ActivePet;
                            Necromancer.NecromancerPets.ReleasePetReference(oldPet,
                                "cast-sweep tier transition");
                            Destroy(oldPet.gameObject);
                            yield return null;
                            yield return null;
                        }

                        if (definition.Kind ==
                            Necromancer.NecromancerSpellKind.SoulTransfusion)
                        {
                            // A live SimPlayer may start walking, zone, or be
                            // reclaimed by its AI between StartSpell and the
                            // shortened QA resolution. The stationary inert
                            // target exercises the identical native cast,
                            // resolve, and custom transfer path deterministically
                            // when temporarily marked as player-like.
                            soulTransfusionTarget = dummyStats;
                            if (soulTransfusionTarget == null)
                                throw new InvalidOperationException(
                                    "no valid allied target is available for Soul Transfusion");
                            soulTargetWasPlayerOrSim =
                                soulTransfusionTarget.PlayerOrSimPlayer;
                            soulTargetMana = soulTransfusionTarget.CurrentMana;
                            soulTransfusionTarget.PlayerOrSimPlayer = true;
                            soulTransfusionTarget.CurrentMana = 0;
                            target = soulTransfusionTarget;
                            spell.SpellRange = 100000f;
                            simManaBefore = target.CurrentMana;
                        }
                        else if (definition.SelfOnly || definition.Kind ==
                            Necromancer.NecromancerSpellKind.SummonPet ||
                            definition.Kind ==
                                Necromancer.NecromancerSpellKind.HealthToMana ||
                            definition.Kind ==
                                Necromancer.NecromancerSpellKind.ReclaimPet ||
                            definition.Kind ==
                                Necromancer.NecromancerSpellKind.FeignDeath ||
                            definition.Kind ==
                                Necromancer.NecromancerSpellKind.Harmshield)
                        {
                            target = player;
                        }
                        else if (definition.Kind ==
                            Necromancer.NecromancerSpellKind.PetHeal ||
                            definition.Kind ==
                                Necromancer.NecromancerSpellKind.PetBuff)
                        {
                            target = Necromancer.NecromancerPets.ActivePet?
                                .GetComponent<Stats>();
                            if (target == null)
                                throw new InvalidOperationException(
                                    "the controlled bone thrall is unavailable");
                        }
                        else
                        {
                            target = dummyStats;
                            if (target == null || !target.Myself.Alive)
                                throw new InvalidOperationException(
                                    "the controlled hostile test target is unavailable");
                        }

                        if (definition.Pbaoe && dummyObject != null)
                        {
                            dummyObject.transform.position =
                                GameData.PlayerControl.transform.position +
                                GameData.PlayerControl.transform.forward * 3f;
                        }

                        if (definition.Kind ==
                            Necromancer.NecromancerSpellKind.PetHeal)
                        {
                            Stats petStats = Necromancer.NecromancerPets.ActivePet?
                                .GetComponent<Stats>();
                            if (petStats != null) petStats.CurrentHP = 1;
                        }
                        else if (target != null && target != player &&
                            temporarySim == null)
                        {
                            target.CurrentHP = 100000;
                        }

                        if (definition.Kind ==
                            Necromancer.NecromancerSpellKind.HealthToMana ||
                            definition.Kind ==
                                Necromancer.NecromancerSpellKind.ReclaimPet)
                            player.CurrentMana = 0;
                        else
                            player.CurrentMana = Mathf.Max(2000,
                                spell.ManaCost + 500);
                        // Lifetaps must have missing health available so the
                        // real native heal can be observed independently from
                        // their mana cost. Other casts retain the oversized QA
                        // health reserve used by the all-spell sweep.
                        player.CurrentHP = definition.Lifetap
                            ? Mathf.Max(1, player.CurrentMaxHP / 2)
                            : definition.Kind == Necromancer.NecromancerSpellKind
                                .HealthToMana
                                ? player.CurrentMaxHP
                                : Mathf.Min(10000, player.CurrentMaxHP);
                        playerHpBefore = player.CurrentHP;
                        playerManaBefore = player.CurrentMana;
                        targetHpBefore = target?.CurrentHP ?? 0;

                        // Twelve Erenshor cast ticks is roughly 0.2 seconds:
                        // enough to render the real charge effect without
                        // turning a 35-spell QA sweep into several minutes.
                        spell.SpellChargeTime = 12f;
                        spell.ResistModifier = 10000f;
                        int resolutionBefore =
                            Necromancer.NecromancerRuntime.ResolutionCount;
                        _gearVisualLabel = $"Necromancer Real Cast {index + 1}/" +
                            $"{sequence.Count}\n{spell.SpellName}";
                        Stats castTarget = definition.Pbaoe ? player : target;
                        int startWaitFrames = 0;
                        while (!started && startWaitFrames++ < 90)
                        {
                            started = definition.Pbaoe
                                ? caster.StartSpell(spell, castTarget)
                                : caster.StartSpell(spell, castTarget, 0f);
                            if (!started) yield return null;
                        }
                        int waitFrames = 0;
                        while (started &&
                            Necromancer.NecromancerRuntime.ResolutionCount <=
                                resolutionBefore && waitFrames++ < 300)
                            yield return null;
                        resolved = started &&
                            Necromancer.NecromancerRuntime.ResolutionCount >
                                resolutionBefore &&
                            ReferenceEquals(
                                Necromancer.NecromancerRuntime.LastResolvedSpell,
                                spell) &&
                            string.IsNullOrEmpty(
                                Necromancer.NecromancerRuntime.LastResolutionError);
                        int resetWait = 0;
                        while (caster.isCasting() && resetWait++ < 60)
                            yield return null;

                        // Pet configuration itself yields two frames after the
                        // native summon resolves.
                        if (definition.Kind ==
                            Necromancer.NecromancerSpellKind.SummonPet)
                        {
                            int petWaitFrames = 0;
                            while ((Necromancer.NecromancerPets.ActivePet == null ||
                                Necromancer.NecromancerPets.ActivePet
                                    .GetComponent<Necromancer.NecromancerPetMarker>()
                                    ?.Tier != definition.PetTier) &&
                                petWaitFrames++ < 90)
                                yield return null;
                            StageNecromancerCapturePet(
                                Necromancer.NecromancerPets.ActivePet);
                        }
                        if (definition.Pbaoe)
                        {
                            // Erenshor's AE collector applies collision damage
                            // and linked status effects on subsequent physics
                            // ticks rather than inside ResolveSpell itself. Its
                            // exact arrival varies with the current render-to-
                            // physics cadence, so wait for the authored effect
                            // instead of assuming a fixed two-tick latency.
                            Spell expectedPbaoeEffect = string.IsNullOrEmpty(
                                    definition.AppliedEffectKey)
                                ? null
                                : Necromancer.NecromancerCatalog.GetSpell(
                                    definition.AppliedEffectKey);
                            int pbaoeWaitTicks = 0;
                            do
                            {
                                yield return new WaitForFixedUpdate();
                            }
                            while (expectedPbaoeEffect != null &&
                                target?.CheckForStatus(expectedPbaoeEffect) != true &&
                                pbaoeWaitTicks++ < 20);
                        }
                        behavior = resolved && ValidateNecromancerCastOutcome(
                            definition, spell, target, player, targetHpBefore,
                            playerHpBefore, playerManaBefore,
                            soulTransfusionTarget,
                            simManaBefore, out behaviorDetail);

                        if (definition.Pbaoe && dummyObject != null)
                        {
                            dummyObject.transform.position = dummyPosition;
                            dummyObject.transform.rotation = dummyRotation;
                            yield return null;
                        }

                        yield return new WaitForSecondsRealtime(0.34f);
                        HideTransientCaptureCanvases(captureState);
                        yield return new WaitForEndOfFrame();
                        string screenshot = Path.Combine(directory,
                            $"{index + 1:D2}_" +
                            SafeFileComponent(spell.SpellName) + ".png");
                        bool requested = RequestScreenshot(screenshot,
                            out string screenshotError);
                        int screenshotWait = 0;
                        while (requested && !ScreenshotExists(screenshot) &&
                            screenshotWait++ < 120)
                            yield return null;
                        screenshotExists = requested &&
                            ScreenshotExists(screenshot);
                        detail = behaviorDetail;
                        if (!string.IsNullOrEmpty(screenshotError))
                            detail += "; screenshot=" + screenshotError;
                    }
                    finally
                    {
                        spell.SpellChargeTime = originalCastTime;
                        spell.SpellRange = originalRange;
                        spell.ResistModifier = originalResist;
                        if (soulTransfusionTarget != null)
                        {
                            soulTransfusionTarget.PlayerOrSimPlayer =
                                soulTargetWasPlayerOrSim;
                            soulTransfusionTarget.CurrentMana = soulTargetMana;
                        }
                        if (temporarySim != null)
                            temporarySim.InGroup = simWasInGroup;
                        if (temporarySimNpc != null)
                            temporarySimNpc.InGroup = simNpcWasInGroup;
                    }

                    bool result = started && resolved && behavior &&
                        screenshotExists;
                    SkillsPlugin.Log.LogInfo(
                        $"[Necro Cast QA] {index + 1:D2} {spell.SpellName}: " +
                        $"started={started}, resolved={resolved}, " +
                        $"behavior={behavior}, screenshot={screenshotExists}, " +
                        $"detail={detail}");
                    if (result) passed++; else failed++;
                    manifest.Append(Tsv(spell.SpellName)).Append('\t')
                        .Append(started ? "PASS" : "FAIL").Append('\t')
                        .Append(resolved ? "PASS" : "FAIL").Append('\t')
                        .Append(behavior ? "PASS" : "FAIL").Append('\t')
                        .Append(screenshotExists ? "PASS" : "FAIL").Append('\t')
                        .Append(result ? "PASS" : "FAIL").Append('\t')
                        .AppendLine(Tsv(detail));

                    ClearNecromancerTestEffects(player);
                    Stats activePetStats = Necromancer.NecromancerPets.ActivePet?
                        .GetComponent<Stats>();
                    ClearNecromancerTestEffects(activePetStats);
                    ClearNecromancerTestEffects(dummyStats);
                    yield return null;
                    Necromancer.NecromancerRuntime.Tick();
                    if ((index + 1) % 5 == 0 || index + 1 == sequence.Count)
                        ChatHelper.Send(
                            $"<color=#AAAAAA>[Test Mode]</color> Real casts " +
                            $"{index + 1}/{sequence.Count}: {passed} pass, " +
                            $"{failed} fail.");
                }

                string manifestPath = Path.Combine(directory, "manifest.tsv");
                if (!SafeFileIO.TryWriteAllTextAtomic(manifestPath,
                    manifest.ToString(), out string writeError))
                    throw new IOException(
                        "could not write cast manifest: " + writeError);
                _lastReportPath = manifestPath;
                ChatHelper.Send(
                    $"<color={(failed == 0 ? "#4CAF50" : "#EF5350")}>" +
                    $"[Test Mode] Real Necromancer cast sweep complete: " +
                    $"{passed} passed, {failed} failed.</color>");
                ChatHelper.Send(
                    "<color=#AAAAAA>Evidence: " + directory + "</color>");
            }
            finally
            {
                NPC pet = Necromancer.NecromancerPets.ActivePet;
                if (pet != null)
                {
                    Necromancer.NecromancerPets.ReleasePetReference(pet,
                        "cast sweep cleanup");
                    Destroy(pet.gameObject);
                }
                GameData.PlayerControl?.Myself?.NearbyEnemies?
                    .Remove(dummyObject?.GetComponent<Character>());
                GameData.PlayerCombat?.MyMeleeRange?.GetNPCsInRange()?
                    .Remove(dummyObject?.GetComponent<Character>());
                if (dummyObject != null) Destroy(dummyObject);
                ClearNecromancerTestEffects(player);
                Necromancer.NecromancerRuntime.ResetCharacterState();
                if (player != null)
                {
                    player.BaseHP = originalBaseHp;
                    player.CalcStats();
                    player.CurrentHP = Mathf.Clamp(originalHp, 1,
                        player.CurrentMaxHP);
                    player.CurrentMana = originalMana;
                    player.Myself.Alive = true;
                }
                if (GameData.PlayerControl?.Myself != null)
                    GameData.PlayerControl.Myself.Invulnerable =
                        originalInvulnerable;
                EndNecromancerVisualCapture(captureState);
                _gearVisualLabel = null;
                _necromancerCastRunning = false;
            }
        }

        private static bool ValidateNecromancerCastOutcome(
            Necromancer.NecromancerSpellDefinition definition, Spell spell,
            Stats target, Stats player, int targetHpBefore, int playerHpBefore,
            int playerManaBefore, Stats simTarget, int simManaBefore,
            out string detail)
        {
            bool valid;
            switch (definition.Kind)
            {
                case Necromancer.NecromancerSpellKind.SummonPet:
                    Necromancer.NecromancerPetMarker marker =
                        Necromancer.NecromancerPets.ActivePet?
                            .GetComponent<Necromancer.NecromancerPetMarker>();
                    valid = marker != null && marker.Tier == definition.PetTier;
                    detail = valid ? $"native pet spawned and configured at tier {marker.Tier}"
                        : "native pet did not spawn/configure at the expected tier";
                    return valid;
                case Necromancer.NecromancerSpellKind.PetHeal:
                    Stats healedPet = Necromancer.NecromancerPets.ActivePet?
                        .GetComponent<Stats>();
                    valid = healedPet != null && healedPet.CurrentHP > targetHpBefore;
                    detail = $"pet HP {targetHpBefore}->{healedPet?.CurrentHP ?? -1}";
                    return valid;
                case Necromancer.NecromancerSpellKind.HealthToMana:
                    valid = player.CurrentHP < playerHpBefore &&
                        player.CurrentMana > playerManaBefore;
                    detail = $"HP {playerHpBefore}->{player.CurrentHP}; mana " +
                        $"{playerManaBefore}->{player.CurrentMana}";
                    return valid;
                case Necromancer.NecromancerSpellKind.ReclaimPet:
                    valid = Necromancer.NecromancerPets.ActivePet == null &&
                        player.CurrentMana > playerManaBefore;
                    detail = $"pet cleared={Necromancer.NecromancerPets.ActivePet == null}; " +
                        $"mana {playerManaBefore}->{player.CurrentMana}";
                    return valid;
                case Necromancer.NecromancerSpellKind.FeignDeath:
                    valid = player.CheckForStatus(spell) &&
                        Necromancer.NecromancerRuntime.BlocksAggro(player.Myself);
                    detail = $"status={player.CheckForStatus(spell)}; aggroBlocked=" +
                        Necromancer.NecromancerRuntime.BlocksAggro(player.Myself);
                    return valid;
                case Necromancer.NecromancerSpellKind.SoulTransfusion:
                    valid = simTarget != null && simTarget.CurrentMana > simManaBefore;
                    detail = $"SimPlayer mana {simManaBefore}->{simTarget?.CurrentMana ?? -1}";
                    return valid;
                case Necromancer.NecromancerSpellKind.PetBuff:
                    Stats buffedPet = Necromancer.NecromancerPets.ActivePet?
                        .GetComponent<Stats>();
                    valid = buffedPet?.CheckForStatus(spell) == true;
                    detail = "pet status=" + valid;
                    return valid;
                case Necromancer.NecromancerSpellKind.Harmshield:
                    valid = player.CheckForStatus(spell) &&
                        player.Myself.Invulnerable;
                    detail = $"status={player.CheckForStatus(spell)}; " +
                        $"invulnerable={player.Myself.Invulnerable}";
                    return valid;
            }

            if (definition.SelfOnly || spell.Type == Spell.SpellType.Beneficial)
            {
                valid = player.CheckForStatus(spell);
                detail = "self status=" + valid;
                return valid;
            }
            if (definition.Lifetap)
            {
                bool targetDrained = target != null &&
                    target.CurrentHP < targetHpBefore;
                bool casterHealed = player.CurrentHP > playerHpBefore;
                bool appliedEffect = string.IsNullOrEmpty(
                        definition.AppliedEffectKey) ||
                    target?.CheckForStatus(Necromancer.NecromancerCatalog
                        .GetSpell(definition.AppliedEffectKey)) == true;
                // Mana regeneration is an independent native character tick.
                // A regen pulse can land during the real cast and legitimately
                // leave the caster above this pre-cast snapshot, so mana delta
                // cannot prove that a lifetap restored mana. The spell contract
                // is fully covered by damage, healing, and its secondary effect;
                // dedicated covenant/transfusion cases verify mana restoration.
                valid = targetDrained && casterHealed && appliedEffect;
                detail = $"lifetap target HP {targetHpBefore}->" +
                    $"{target?.CurrentHP ?? -1}; caster HP {playerHpBefore}->" +
                    $"{player.CurrentHP}; mana {playerManaBefore}->" +
                    $"{player.CurrentMana} (ambient regen allowed); " +
                    $"secondaryEffect={appliedEffect}";
                return valid;
            }
            if (!string.IsNullOrEmpty(definition.AppliedEffectKey))
            {
                Spell applied = Necromancer.NecromancerCatalog.GetSpell(
                    definition.AppliedEffectKey);
                valid = target?.CheckForStatus(applied) == true;
                detail = $"applied effect {applied?.SpellName ?? "<missing>"}=" +
                    valid;
                return valid;
            }
            if (definition.DurationTicks > 0)
            {
                valid = target?.CheckForStatus(spell) == true;
                detail = "target status=" + valid;
                return valid;
            }
            if (definition.TargetDamage > 0)
            {
                valid = target != null && target.CurrentHP < targetHpBefore;
                detail = $"target HP {targetHpBefore}->{target?.CurrentHP ?? -1}";
                return valid;
            }
            detail = "resolved through native pipeline";
            return true;
        }

        private static void ClearNecromancerTestEffects(Stats stats)
        {
            if (stats == null) return;
            Necromancer.NecromancerRuntime.CancelCovenantForTesting(stats);
            for (int index = stats.StatusEffects.Length - 1; index >= 0; index--)
            {
                Spell effect = stats.StatusEffects[index]?.Effect;
                if (Necromancer.NecromancerCatalog.TryGetDefinition(effect,
                    out _))
                {
                    try { stats.RemoveStatusEffect(index); }
                    catch { }
                }
            }
            foreach (Necromancer.NecromancerTimedVfx effect in stats
                .GetComponentsInChildren<Necromancer.NecromancerTimedVfx>(true))
                if (effect != null) Destroy(effect.gameObject);
        }

        private void StartSmoke()
        {
            if (_smokeRunning || _craftedIconCaptureRunning)
            {
                ChatHelper.Send(
                    "<color=#FFD54F>[Test Mode]</color> A full smoke run is already active.");
                return;
            }
            if (!RequireLoadedWorld()) return;
            if (!Items.ItemFactory.Initialized)
            {
                ChatHelper.Send(
                    "<color=#EF5350>[Test Mode]</color> Item database is not initialized yet.");
                return;
            }
            if (Items.BagWindow.AnyOpen)
            {
                ChatHelper.Send(
                    "<color=#EF5350>[Test Mode]</color> Close all addon bag windows before running smoke tests.");
                return;
            }
            _smokeRunning = true;
            StartCoroutine(RunFullSmoke());
        }

        private void StartGearSmoke(string filter)
        {
            if (_smokeRunning || _craftedIconCaptureRunning)
            {
                ChatHelper.Send(
                    "<color=#FFD54F>[Test Mode]</color> A smoke run is already active.");
                return;
            }
            if (!RequireLoadedWorld()) return;
            if (!Items.ItemFactory.Initialized)
            {
                ChatHelper.Send(
                    "<color=#EF5350>[Test Mode]</color> Item database is not initialized yet.");
                return;
            }
            _gearFilter = (filter ?? "").Trim();
            _smokeRunning = true;
            StartCoroutine(RunGearOnly());
        }

        private IEnumerator RunFullSmoke()
        {
            IEnumerator core = RunFullSmokeCore();
            while (true)
            {
                bool moved;
                object current = null;
                try
                {
                    moved = core.MoveNext();
                    if (moved) current = core.Current;
                }
                catch (Exception ex)
                {
                    (core as IDisposable)?.Dispose();
                    SkillsPlugin.Log.LogError($"Full smoke run aborted: {ex}");
                    ChatHelper.Send(
                        $"<color=#EF5350>[Test Mode]</color> Smoke run aborted safely: {ex.Message}");
                    break;
                }
                if (!moved) break;
                yield return current;
            }
            _smokeRunning = false;
        }

        private IEnumerator RunGearOnly()
        {
            IEnumerator core = RunGearOnlyCore();
            while (true)
            {
                bool moved;
                object current = null;
                try
                {
                    moved = core.MoveNext();
                    if (moved) current = core.Current;
                }
                catch (Exception ex)
                {
                    (core as IDisposable)?.Dispose();
                    SkillsPlugin.Log.LogError($"Gear visual smoke aborted: {ex}");
                    ChatHelper.Send(
                        $"<color=#EF5350>[Test Mode]</color> Gear visual run aborted safely: {ex.Message}");
                    break;
                }
                if (!moved) break;
                yield return current;
            }
            _smokeRunning = false;
        }

        private IEnumerator RunGearOnlyCore()
        {
            var report = new SmokeReport();
            List<CatalogEntry> catalog = BuildCatalog(false);
            List<CatalogEntry> gear = GetEquippableGear(catalog);
            if (!string.IsNullOrEmpty(_gearFilter))
                gear.RemoveAll(entry => entry.Name.IndexOf(_gearFilter,
                    StringComparison.OrdinalIgnoreCase) < 0);
            string runId = $"{DateTime.UtcNow:yyyyMMdd-HHmmss}-{SafeReportName(SkillsSaveManager.CharName)}";
            string reportDirectory = Path.Combine(RuntimePaths.DataDirectory,
                "TestReports");
            string visualDirectory = Path.Combine(reportDirectory,
                "GearVisuals", runId);
            ChatHelper.Send(
                $"<color=#FFD54F>[Test Mode]</color> Live gear visual run started for {gear.Count} items" +
                (string.IsNullOrEmpty(_gearFilter) ? "." : $" matching '{_gearFilter}'."));
            IEnumerator visuals = RunGearVisualChecks(report, gear, visualDirectory);
            while (visuals.MoveNext()) yield return visuals.Current;
            report.Check("CATALOG", "equippable gear count", gear.Count > 0,
                $"actual={gear.Count}");
            string path = Path.Combine(reportDirectory, $"gear-{runId}.txt");
            string content = report.Finish(gear.Count, 0);
            if (!SafeFileIO.TryWriteAllTextAtomic(path, content,
                out string writeError))
                throw new IOException("could not write gear report: " + writeError);
            _lastReportPath = path;
            SkillsPlugin.Log.LogInfo(
                $"Gear visual report: {report.Passed} passed, {report.Failed} failed, path={path}");
            ChatHelper.Send(
                $"<color={(report.Failed == 0 ? "#4CAF50" : "#EF5350")}>[Test Mode]</color> " +
                $"Gear visual run complete: {report.Passed} passed, {report.Failed} failed; " +
                $"evidence={visualDirectory}");
            _gearFilter = null;
        }

        private IEnumerator RunFullSmokeCore()
        {
            var report = new SmokeReport();
            int recipeCount = 0;
            List<CatalogEntry> catalog = null;
            string runId = $"{DateTime.UtcNow:yyyyMMdd-HHmmss}-{SafeReportName(SkillsSaveManager.CharName)}";
            string reportDirectory = Path.Combine(RuntimePaths.DataDirectory,
                "TestReports");
            string visualDirectory = Path.Combine(reportDirectory,
                "GearVisuals", runId);
            ChatHelper.Send(
                "<color=#FFD54F>[Test Mode]</color> Full in-game smoke run started. " +
                "Temporary probes and equipment will be restored; visual evidence " +
                "is captured for every equippable addon item.");
            Stations.RecipeVendors.Initialize();
            catalog = BuildCatalog(false);
            RunFeatureChecks(report);
            IEnumerator utilityHudPauseGuard =
                RunUtilityHudPauseGuardCheck(report);
            while (utilityHudPauseGuard.MoveNext())
                yield return utilityHudPauseGuard.Current;

            var duplicateIds = FindDuplicateIds(catalog);
            for (int i = 0; i < catalog.Count; i++)
            {
                CatalogEntry entry = catalog[i];
                var errors = ValidateItem(entry, duplicateIds);
                bool inventory = Items.BagSystem.ProbeInventoryRoundTrip(
                    entry.Item, out string inventoryError);
                bool bag = Items.BagSystem.ProbeBagRoundTrip(
                    entry.Item, out string bagError);
                if (!inventory) errors.Add("inventory: " + inventoryError);
                if (!bag) errors.Add("bag: " + bagError);
                report.Check("ITEM", entry.Name, errors.Count == 0,
                    errors.Count == 0
                        ? $"source={entry.Source}; id={entry.Item.Id}; inventory+bag round-trip"
                        : string.Join("; ", errors));

                if ((i + 1) % ItemsPerFrame == 0)
                    yield return null;
            }

            recipeCount = RunRecipeChecks(report);
            report.Check("CATALOG", "expected recipe count",
                recipeCount == ExpectedRecipeCount,
                $"actual={recipeCount}; expected={ExpectedRecipeCount}");

            List<CatalogEntry> gear = GetEquippableGear(catalog);
            IEnumerator visuals = RunGearVisualChecks(report, gear, visualDirectory);
            while (visuals.MoveNext()) yield return visuals.Current;
            report.Check("CATALOG", "equippable gear count", gear.Count > 0,
                $"actual={gear.Count}; evidence={visualDirectory}");

            string content = report.Finish(catalog.Count, recipeCount);
            string path = Path.Combine(reportDirectory, $"smoke-{runId}.txt");
            if (!SafeFileIO.TryWriteAllTextAtomic(path, content,
                out string writeError))
                throw new IOException("could not write smoke report: " + writeError);
            _lastReportPath = path;
            SkillsPlugin.Log.LogInfo(
                $"Full smoke report: {report.Passed} passed, {report.Failed} failed, path={path}");
            ChatHelper.Send(
                $"<color={(report.Failed == 0 ? "#4CAF50" : "#EF5350")}>[Test Mode]</color> " +
                $"Smoke complete: {report.Passed} passed, {report.Failed} failed; " +
                $"{catalog.Count} items and {recipeCount} recipes checked.");
            ChatHelper.Send($"<color=#AAAAAA>Report: {path}</color>");
        }

        private IEnumerator RunUtilityHudPauseGuardCheck(SmokeReport report)
        {
            GameObject escapeMenu = GameData.GM?.EscapeMenu;
            UtilityActionBar actionBar =
                SkillsPlugin.Instance?.GetComponent<UtilityActionBar>();
            if (escapeMenu == null || actionBar == null)
            {
                report.Check("FEATURE", "utility HUD pause-menu guard", false,
                    $"escapeMenu={escapeMenu != null}; actionBar={actionBar != null}");
                yield break;
            }

            bool wasActive = escapeMenu.activeSelf;
            try
            {
                escapeMenu.SetActive(true);
            }
            catch (Exception ex)
            {
                report.Check("FEATURE", "utility HUD pause-menu guard", false,
                    "could not activate escape menu: " + ex.Message);
                yield break;
            }

            yield return null;
            yield return null;

            try
            {
                bool detected = SkillsPlugin.IsEscapeMenuOpen;
                bool hidden = !actionBar.IsVisibleForTesting;
                report.Check("FEATURE", "utility HUD pause-menu guard",
                    detected && hidden,
                    $"self={escapeMenu.activeSelf}; " +
                    $"hierarchy={escapeMenu.activeInHierarchy}; " +
                    $"detected={detected}; hudVisible={actionBar.IsVisibleForTesting}");
            }
            catch (Exception ex)
            {
                report.Check("FEATURE", "utility HUD pause-menu guard", false,
                    "probe failed: " + ex.Message);
            }
            finally
            {
                try { escapeMenu.SetActive(wasActive); }
                catch (Exception ex)
                {
                    SkillsPlugin.Log.LogWarning(
                        "Could not restore escape menu after HUD smoke probe: " + ex.Message);
                }
            }

            yield return null;
        }

        private IEnumerator RunGearVisualChecks(SmokeReport report,
            List<CatalogEntry> gear, string visualDirectory)
        {
            Directory.CreateDirectory(visualDirectory);
            var manifest = new StringBuilder();
            manifest.AppendLine(
                "index\titem\tdeclared_slot\ttarget_slot\tmodel\trender_check\tscreenshot\tresult\tdetail");
            EquipmentSnapshot snapshot = CaptureEquipmentSnapshot();
            int visualSettleFrames = GetVisualSettleFrameCount();
            SkillsPlugin.Log.LogInfo(
                $"Gear visual smoke: waiting {visualSettleFrames} frames for each native modular refresh.");
            _gearVisualRunning = true;
            var cameraHooks = new List<GearCaptureCameraHook>();
            try
            {
                if (GameData.PlayerInv.CharCam != null)
                    GameData.PlayerInv.CharCam.SetActive(true);
                GameData.HidePlayerHelm = 0;
                Camera[] gameCameras = Camera.allCameras;
                if (gameCameras.Length == 0)
                    gameCameras = UnityEngine.Object.FindObjectsOfType<Camera>();
                if (GameData.PlayerInv.Modulars != null)
                {
                    foreach (Camera gameCamera in gameCameras)
                    {
                        if (gameCamera == null || gameCamera.orthographic)
                            continue;
                        GearCaptureCameraHook cameraHook =
                            gameCamera.GetComponent<GearCaptureCameraHook>() ??
                            gameCamera.gameObject.AddComponent<GearCaptureCameraHook>();
                        cameraHook.Target = GameData.PlayerInv.Modulars.transform;
                        cameraHook.ForceFront = false;
                        cameraHooks.Add(cameraHook);
                    }
                }

                for (int i = 0; i < gear.Count; i++)
                {
                    CatalogEntry entry = gear[i];
                    Item item = entry.Item;
                    var errors = new List<string>();
                    ItemIcon target = PrepareGearForVisual(item, errors);
                    string targetSlot = target == null
                        ? "<none>" : target.ThisSlotType.ToString();
                    _gearVisualLabel =
                        $"Classic Skills Gear Visual {i + 1}/{gear.Count}\n" +
                        $"{item.ItemName}  |  declared {item.RequiredSlot}  |  " +
                        $"equipped {targetSlot}  |  BACK VIEW";

                    // On the live player root, Erenshor's ModularParts.UpdateSlot
                    // yields once per child transform. Wait for the longest
                    // attachment hierarchy so a late-listed model is not
                    // mistaken for a missing one and no prior item remains.
                    for (int settle = 0; settle < visualSettleFrames; settle++)
                        yield return null;
                    yield return new WaitForEndOfFrame();

                    bool rendered = ValidateEquippedVisual(item, target,
                        out string visualDetail);
                    if (!rendered) errors.Add(visualDetail);
                    GetExpectedVisualRoots(item, target, out _,
                        out bool baseGameRendersSlot);

                    string screenshotStem =
                        $"{i + 1:D4}_{item.RequiredSlot}_{SafeFileComponent(item.ItemName)}";
                    string backName = screenshotStem + "_back.png";
                    string backPath = Path.Combine(visualDirectory, backName);
                    if (!RequestScreenshot(backPath, out string backRequestError))
                        errors.Add("back screenshot request failed: " + backRequestError);
                    yield return new WaitForEndOfFrame();
                    int backWaitFrames = 0;
                    while (!ScreenshotExists(backPath) && backWaitFrames++ < 120)
                        yield return null;
                    if (!ScreenshotExists(backPath))
                        errors.Add("back visual evidence image was not written");

                    string frontName = "";
                    if (baseGameRendersSlot && GameData.PlayerInv.Modulars != null)
                    {
                        if (cameraHooks.Count == 0)
                            errors.Add("front-view camera hook is unavailable");
                        else
                            foreach (GearCaptureCameraHook cameraHook in cameraHooks)
                                cameraHook.ForceFront = true;
                        _gearVisualLabel =
                            $"Classic Skills Gear Visual {i + 1}/{gear.Count}\n" +
                            $"{item.ItemName}  |  declared {item.RequiredSlot}  |  " +
                            $"equipped {targetSlot}  |  FRONT VIEW";
                        yield return null;
                        yield return new WaitForEndOfFrame();
                        frontName = screenshotStem + "_front.png";
                        string frontPath = Path.Combine(visualDirectory, frontName);
                        if (!RequestScreenshot(frontPath,
                            out string frontRequestError))
                            errors.Add("front screenshot request failed: " + frontRequestError);
                        yield return new WaitForEndOfFrame();
                        int frontWaitFrames = 0;
                        while (!ScreenshotExists(frontPath) &&
                            frontWaitFrames++ < 120)
                            yield return null;
                        if (!ScreenshotExists(frontPath))
                            errors.Add("front visual evidence image was not written");
                        foreach (GearCaptureCameraHook cameraHook in cameraHooks)
                            cameraHook.ForceFront = false;
                        GameData.PlayerInv.Modulars.transform.localRotation =
                            snapshot.PlayerModelRotation;
                    }

                    bool passed = errors.Count == 0;
                    string screenshots = string.IsNullOrEmpty(frontName)
                        ? backName : backName + "," + frontName;
                    string detail =
                        $"declared={item.RequiredSlot}; target={targetSlot}; " +
                        $"model={item.EquipmentToActivate ?? "<none>"}; " +
                        $"visual={visualDetail}; screenshots={screenshots}";
                    if (!passed) detail += "; " + string.Join("; ", errors);
                    report.Check("GEAR", item.ItemName, passed, detail);
                    manifest.Append(i + 1).Append('\t')
                        .Append(Tsv(item.ItemName)).Append('\t')
                        .Append(item.RequiredSlot).Append('\t')
                        .Append(targetSlot).Append('\t')
                        .Append(Tsv(item.EquipmentToActivate ?? "")).Append('\t')
                        .Append(Tsv(visualDetail)).Append('\t')
                        .Append(screenshots).Append('\t')
                        .Append(passed ? "PASS" : "FAIL").Append('\t')
                        .AppendLine(Tsv(errors.Count == 0
                            ? "" : string.Join("; ", errors)));

                    if ((i + 1) % 20 == 0 || i + 1 == gear.Count)
                        ChatHelper.Send(
                            $"<color=#AAAAAA>[Test Mode]</color> Gear visuals {i + 1}/{gear.Count} captured.");
                    yield return null;
                }
            }
            finally
            {
                RemoveAddonWornEffectsForVisualCapture();
                bool restored = RestoreEquipmentSnapshot(snapshot);
                bool persisted = restored && PersistRestoredNativeSnapshot();
                report.Check("GEAR STATE",
                    "original equipment and vitals restored and saved",
                    restored && persisted,
                    $"restored={restored}; nativeSave={persisted}");
                foreach (GearCaptureCameraHook cameraHook in cameraHooks)
                {
                    cameraHook.ForceFront = false;
                    cameraHook.Target = null;
                    Destroy(cameraHook);
                }
                _gearVisualRunning = false;
                _gearVisualLabel = null;
                string manifestPath = Path.Combine(visualDirectory, "manifest.tsv");
                if (!SafeFileIO.TryWriteAllTextAtomic(manifestPath,
                    manifest.ToString(), out string manifestError))
                    SkillsPlugin.Log.LogError(
                        "Gear visual manifest could not be written: " + manifestError);
            }
        }

        private static EquipmentSnapshot CaptureEquipmentSnapshot()
        {
            var snapshot = new EquipmentSnapshot
            {
                Aura = GameData.PlayerInv.AuraSlot?.MyItem,
                AuraQuantity = GameData.PlayerInv.AuraSlot?.Quantity ?? 1,
                Charm = GameData.PlayerInv.CharmSlot?.MyItem,
                CharmQuantity = GameData.PlayerInv.CharmSlot?.Quantity ?? 1,
                CharacterCameraActive = GameData.PlayerInv.CharCam != null &&
                    GameData.PlayerInv.CharCam.activeSelf,
                HidePlayerHelm = GameData.HidePlayerHelm,
                PlayerModelRotation = GameData.PlayerInv.Modulars != null
                    ? GameData.PlayerInv.Modulars.transform.localRotation
                    : Quaternion.identity,
                CurrentHp = GameData.PlayerStats?.CurrentHP ?? 1,
                CurrentMana = GameData.PlayerStats?.CurrentMana ?? 0
            };
            foreach (ItemIcon slot in GameData.PlayerInv.EquipmentSlots)
            {
                snapshot.Items.Add(slot?.MyItem);
                snapshot.Quantities.Add(slot?.Quantity ?? 1);
            }
            return snapshot;
        }

        private static bool RestoreEquipmentSnapshot(EquipmentSnapshot snapshot)
        {
            if (snapshot == null || GameData.PlayerInv == null) return false;
            try
            {
                int count = Mathf.Min(snapshot.Items.Count,
                    GameData.PlayerInv.EquipmentSlots.Count);
                for (int i = 0; i < count; i++)
                {
                    ItemIcon slot = GameData.PlayerInv.EquipmentSlots[i];
                    if (slot == null) continue;
                    slot.MyItem = snapshot.Items[i] ?? GameData.PlayerInv.Empty;
                    slot.Quantity = snapshot.Quantities[i];
                }
                if (GameData.PlayerInv.AuraSlot != null)
                {
                    GameData.PlayerInv.AuraSlot.MyItem =
                        snapshot.Aura ?? GameData.PlayerInv.Empty;
                    GameData.PlayerInv.AuraSlot.Quantity = snapshot.AuraQuantity;
                }
                if (GameData.PlayerInv.CharmSlot != null)
                {
                    GameData.PlayerInv.CharmSlot.MyItem =
                        snapshot.Charm ?? GameData.PlayerInv.Empty;
                    GameData.PlayerInv.CharmSlot.Quantity = snapshot.CharmQuantity;
                }
                GameData.HidePlayerHelm = snapshot.HidePlayerHelm;
                if (GameData.PlayerInv.Modulars != null)
                    GameData.PlayerInv.Modulars.transform.localRotation =
                        snapshot.PlayerModelRotation;
                GameData.PlayerInv.UpdatePlayerInventory();
                if (GameData.PlayerStats != null)
                {
                    GameData.PlayerStats.CurrentHP = Mathf.Clamp(
                        snapshot.CurrentHp, 1,
                        Mathf.Max(1, GameData.PlayerStats.CurrentMaxHP));
                    GameData.PlayerStats.CurrentMana = Mathf.Max(0,
                        snapshot.CurrentMana);
                }
                if (GameData.PlayerInv.CharCam != null)
                    GameData.PlayerInv.CharCam.SetActive(
                        snapshot.CharacterCameraActive);
                return true;
            }
            catch (Exception ex)
            {
                SkillsPlugin.Log.LogError(
                    "Test mode could not restore equipment exactly: " + ex);
                return false;
            }
        }

        private static bool PersistRestoredNativeSnapshot()
        {
            try
            {
                if (GameData.GM == null)
                    throw new InvalidOperationException(
                        "game save manager is unavailable");
                // A long visual run can overlap Erenshor's normal autosave.
                // Persist only after restoration so a mid-run temporary
                // equipment layout can never become the next login state.
                GameData.GM.SaveGameData(wScene: false);
                return true;
            }
            catch (Exception ex)
            {
                SkillsPlugin.Log.LogError(
                    "Test mode could not persist the restored native snapshot: " +
                    ex.Message);
                return false;
            }
        }

        private static ItemIcon PrepareGearForVisual(Item item,
            List<string> errors)
        {
            Inventory inventory = GameData.PlayerInv;
            RemoveAddonWornEffectsForVisualCapture();
            foreach (ItemIcon slot in inventory.EquipmentSlots)
            {
                if (slot == null) continue;
                slot.MyItem = inventory.Empty;
                slot.Quantity = 1;
            }
            if (inventory.AuraSlot != null)
            {
                inventory.AuraSlot.MyItem = inventory.Empty;
                inventory.AuraSlot.Quantity = 1;
            }
            if (inventory.CharmSlot != null)
            {
                inventory.CharmSlot.MyItem = inventory.Empty;
                inventory.CharmSlot.Quantity = 1;
            }
            inventory.EquippedItems.Clear();
            foreach (ItemIcon slot in inventory.EquipmentSlots)
                inventory.EquippedItems.Add(slot?.MyItem ?? inventory.Empty);

            ItemIcon target = FindEquipmentTarget(item);
            if (target == null)
            {
                errors.Add("no live equipment slot accepts the declared slot");
                return null;
            }
            bool correctSlot = target.ThisSlotType == item.RequiredSlot ||
                (item.RequiredSlot == Item.SlotType.PrimaryOrSecondary &&
                 (target.ThisSlotType == Item.SlotType.Primary ||
                  target.ThisSlotType == Item.SlotType.Secondary));
            if (!correctSlot) errors.Add("resolved equipment target is the wrong slot");

            if (!PassesNativeEquipEligibility(item, target,
                out string eligibilityError))
                errors.Add("native equip eligibility failed: " + eligibilityError);

            // Equipping is required to exercise Erenshor's real modular model
            // path, but addon worn-effect spells can cover the character in
            // particles and ground decals for several later screenshots. Keep
            // the item equipped while suppressing only that temporary visual
            // noise; the original Spell reference is restored immediately.
            Spell wornEffect = item.WornEffect;
            try
            {
                item.WornEffect = null;
                target.MyItem = item;
                target.Quantity = 1;
                inventory.UpdatePlayerInventory();
                // Gear with HP bonuses changes CurrentMaxHP. Filling the
                // temporary value immediately prevents nearby healer AI from
                // covering subsequent visual evidence in spell particles.
                if (GameData.PlayerStats != null)
                    GameData.PlayerStats.CurrentHP = Mathf.Max(1,
                        GameData.PlayerStats.CurrentMaxHP);
            }
            finally
            {
                item.WornEffect = wornEffect;
            }
            if (!ReferenceEquals(target.MyItem, item))
                errors.Add("item was not retained by the equipment slot");
            if (target != inventory.AuraSlot && target != inventory.CharmSlot &&
                !inventory.EquippedItems.Contains(item))
                errors.Add("native equipped-item state did not include the item");
            return target;
        }

        private static void RemoveAddonWornEffectsForVisualCapture()
        {
            Stats stats = GameData.PlayerStats;
            if (stats?.StatusEffects == null) return;
            for (int i = stats.StatusEffects.Length - 1; i >= 0; i--)
            {
                Spell effect = stats.StatusEffects[i]?.Effect;
                if (effect == null || !effect.WornEffect ||
                    string.IsNullOrEmpty(effect.Id) ||
                    !effect.Id.StartsWith("CS_Worn_",
                        StringComparison.OrdinalIgnoreCase))
                    continue;
                try { stats.RemoveStatusEffect(i); }
                catch (Exception ex)
                {
                    SkillsPlugin.Log.LogWarning(
                        "Test mode could not clear a capture-only worn effect: " +
                        ex.Message);
                }
            }
        }

        private static int GetVisualSettleFrameCount()
        {
            ModularParts parts = GameData.PlayerInv?.Modulars;
            if (parts == null) return 2;
            var roots = new List<Transform>();
            AddRoot(roots, parts.HeadCover);
            AddRoot(roots, parts.FullHeadHelm);
            AddRoot(roots, parts.HatWithHair);
            AddRoot(roots, parts.Cloak);
            AddRoot(roots, parts.ShoulderL);
            AddRoot(roots, parts.ShoulderR);
            AddRoot(roots, parts.WeaponR);
            AddRoot(roots, parts.WeaponL);
            AddRoot(roots, parts.Chest);
            AddRoot(roots, parts.ArmR);
            AddRoot(roots, parts.ArmL);
            AddRoot(roots, parts.BracerL);
            AddRoot(roots, parts.BracerR);
            AddRoot(roots, parts.GloveL);
            AddRoot(roots, parts.GloveR);
            AddRoot(roots, parts.Pants);
            AddRoot(roots, parts.BootsL);
            AddRoot(roots, parts.BootsR);
            int longest = 0;
            foreach (Transform root in roots)
                longest = Mathf.Max(longest,
                    root.GetComponentsInChildren<Transform>(true).Length);
            return Mathf.Clamp(longest + 3, 2, 240);
        }

        private static ItemIcon FindEquipmentTarget(Item item)
        {
            if (item == null || GameData.PlayerInv == null) return null;
            if (item.RequiredSlot == Item.SlotType.Aura)
                return GameData.PlayerInv.AuraSlot;
            if (item.RequiredSlot == Item.SlotType.Charm)
                return GameData.PlayerInv.CharmSlot;
            Item.SlotType targetType =
                item.RequiredSlot == Item.SlotType.PrimaryOrSecondary
                    ? (Items.ItemFactory.IsCraftedOffhandWeapon(item)
                        ? Item.SlotType.Secondary : Item.SlotType.Primary)
                    : item.RequiredSlot;
            foreach (ItemIcon slot in GameData.PlayerInv.EquipmentSlots)
                if (slot != null && slot.ThisSlotType == targetType) return slot;
            return null;
        }

        private static bool PassesNativeEquipEligibility(Item item,
            ItemIcon target, out string error)
        {
            error = null;
            if (EquipmentEligibilityMethod == null)
            {
                error = "ItemIcon.DoInitialChecks(Item, ItemIcon) is unavailable";
                return false;
            }
            ItemIcon source = null;
            foreach (ItemIcon slot in GameData.PlayerInv.StoredSlots)
                if (slot != null && slot.ThisSlotType == Item.SlotType.General)
                { source = slot; break; }
            if (source == null)
            {
                error = "no direct inventory slot is available for the equip probe";
                return false;
            }
            Item original = source.MyItem;
            int originalQuantity = source.Quantity;
            Stats playerStats = GameData.PlayerStats;
            Class originalClass = playerStats?.CharacterClass;
            try
            {
                // John EverQuest is a Necromancer, but the complete visual
                // catalog must still exercise plate, bows, and martial gear.
                // Run only this read-only native eligibility check as the
                // item's first legal class, then restore the QA class before
                // directly staging the visual capture.
                if (playerStats != null && item?.Classes != null &&
                    !item.Classes.Contains(originalClass))
                {
                    Class legalClass = item.Classes
                        .FirstOrDefault(value => value != null);
                    if (legalClass != null)
                        playerStats.CharacterClass = legalClass;
                }
                source.MyItem = item;
                source.Quantity = 1;
                object result = EquipmentEligibilityMethod.Invoke(source,
                    new object[] { item, target });
                if (result is bool allowed && allowed) return true;
                error = "the current game rejected the item/slot pair";
                return false;
            }
            catch (TargetInvocationException ex)
            {
                error = (ex.InnerException ?? ex).Message;
                return false;
            }
            catch (Exception ex)
            {
                error = ex.Message;
                return false;
            }
            finally
            {
                source.MyItem = original;
                source.Quantity = originalQuantity;
                if (playerStats != null)
                    playerStats.CharacterClass = originalClass;
            }
        }

        private static bool ValidateEquippedVisual(Item item, ItemIcon target,
            out string detail)
        {
            if (target == null || !ReferenceEquals(target.MyItem, item))
            {
                detail = "item is not equipped in the resolved slot";
                return false;
            }
            List<Transform> roots = GetExpectedVisualRoots(item, target,
                out bool alternatives, out bool baseGameRendersSlot);
            if (!baseGameRendersSlot)
            {
                detail = "slot-only: Erenshor has no player-model renderer for this slot";
                return true;
            }
            if (Monk.MonkGear.IsHandwrap(item) &&
                string.IsNullOrWhiteSpace(item.EquipmentToActivate))
            {
                return Monk.MonkHandwrapVisuals.ValidateEquippedVisual(
                    target.ThisSlotType, out detail);
            }
            if (string.IsNullOrWhiteSpace(item.EquipmentToActivate))
            {
                detail = "rendered slot has no EquipmentToActivate model";
                return false;
            }
            if (string.Equals(item.EquipmentToActivate, "DEFAULT",
                StringComparison.OrdinalIgnoreCase))
            {
                detail = "rendered slot uses the invisible DEFAULT model";
                return false;
            }
            if (roots.Count == 0)
            {
                detail = "expected player-model attachment root is unavailable";
                return false;
            }
            int activeRoots = 0;
            var paths = new List<string>();
            foreach (Transform root in roots)
            {
                Transform active = FindActiveNamedTransform(root,
                    item.EquipmentToActivate);
                if (active == null) continue;
                activeRoots++;
                paths.Add(GetTransformPath(active));
            }
            bool passed = alternatives ? activeRoots > 0 : activeRoots == roots.Count;
            detail = passed
                ? "active under " + string.Join(", ", paths)
                : $"model '{item.EquipmentToActivate}' active in {activeRoots}/{roots.Count} " +
                  $"expected roots; states={GetNamedVisualStates(roots, item.EquipmentToActivate)}; " +
                  $"available={GetAvailableVisualNames(roots)}";
            return passed;
        }

        private static string GetNamedVisualStates(List<Transform> roots,
            string modelName)
        {
            var states = new List<string>();
            foreach (Transform root in roots)
            {
                if (root == null) continue;
                bool found = false;
                foreach (Transform transform in
                    root.GetComponentsInChildren<Transform>(true))
                {
                    if (!string.Equals(transform.name, modelName,
                        StringComparison.Ordinal)) continue;
                    found = true;
                    states.Add($"{root.name}:rootSelf={root.gameObject.activeSelf}," +
                        $"rootHierarchy={root.gameObject.activeInHierarchy}," +
                        $"modelSelf={transform.gameObject.activeSelf}," +
                        $"modelHierarchy={transform.gameObject.activeInHierarchy}," +
                        $"parent={transform.parent?.name}");
                }
                if (!found)
                    states.Add($"{root.name}:missing");
            }
            return string.Join("|", states);
        }

        private static string GetAvailableVisualNames(List<Transform> roots)
        {
            var names = new HashSet<string>(StringComparer.Ordinal);
            foreach (Transform root in roots)
            {
                if (root == null) continue;
                foreach (Transform transform in
                    root.GetComponentsInChildren<Transform>(true))
                {
                    if (transform == root || string.IsNullOrEmpty(transform.name))
                        continue;
                    names.Add(transform.name);
                }
            }
            var sorted = new List<string>(names);
            sorted.Sort(StringComparer.OrdinalIgnoreCase);
            if (sorted.Count > 40)
                return string.Join(",", sorted.GetRange(0, 40)) +
                    $",...({sorted.Count} total)";
            return string.Join(",", sorted);
        }

        private static List<Transform> GetExpectedVisualRoots(Item item,
            ItemIcon target, out bool alternatives, out bool baseGameRendersSlot)
        {
            alternatives = false;
            baseGameRendersSlot = true;
            var roots = new List<Transform>();
            ModularParts parts = GameData.PlayerInv?.Modulars;
            if (parts == null) return roots;
            switch (item.RequiredSlot)
            {
                case Item.SlotType.Head:
                    alternatives = true;
                    AddRoot(roots, parts.HeadCover);
                    AddRoot(roots, parts.FullHeadHelm);
                    AddRoot(roots, parts.HatWithHair);
                    break;
                case Item.SlotType.Chest: AddRoot(roots, parts.Chest); break;
                case Item.SlotType.Shoulder:
                    AddRoot(roots, parts.ShoulderL); AddRoot(roots, parts.ShoulderR); break;
                case Item.SlotType.Arm:
                    AddRoot(roots, parts.ArmL); AddRoot(roots, parts.ArmR); break;
                case Item.SlotType.Bracer:
                    AddRoot(roots, parts.BracerL); AddRoot(roots, parts.BracerR); break;
                case Item.SlotType.Hand:
                    AddRoot(roots, parts.GloveL); AddRoot(roots, parts.GloveR); break;
                case Item.SlotType.Foot:
                    AddRoot(roots, parts.BootsL); AddRoot(roots, parts.BootsR); break;
                case Item.SlotType.Leg: AddRoot(roots, parts.Pants); break;
                case Item.SlotType.Back: AddRoot(roots, parts.Cloak); break;
                case Item.SlotType.Primary:
                    AddRoot(roots, item.IsBow ? parts.WeaponL : parts.WeaponR); break;
                case Item.SlotType.PrimaryOrSecondary:
                    AddRoot(roots, target.ThisSlotType == Item.SlotType.Secondary
                        ? parts.WeaponL : parts.WeaponR); break;
                case Item.SlotType.Secondary: AddRoot(roots, parts.WeaponL); break;
                default:
                    baseGameRendersSlot = false;
                    break;
            }
            return roots;
        }

        private static void AddRoot(List<Transform> roots, Transform root)
        {
            if (root != null) roots.Add(root);
        }

        private static Transform FindActiveNamedTransform(Transform root,
            string name)
        {
            if (root == null || string.IsNullOrEmpty(name)) return null;
            foreach (Transform transform in root.GetComponentsInChildren<Transform>(true))
                if (string.Equals(transform.name, name, StringComparison.Ordinal) &&
                    transform.gameObject.activeInHierarchy)
                    return transform;
            return null;
        }

        private static string GetTransformPath(Transform transform)
        {
            var names = new List<string>();
            while (transform != null && names.Count < 8)
            {
                names.Add(transform.name);
                transform = transform.parent;
            }
            names.Reverse();
            return string.Join("/", names);
        }

        private static string Tsv(string value)
        {
            return (value ?? "").Replace('\t', ' ').Replace('\r', ' ')
                .Replace('\n', ' ');
        }

        private static bool RequestScreenshot(string path, out string error)
        {
            error = null;
            try
            {
                ScreenCapture.CaptureScreenshot(path);
                return true;
            }
            catch (Exception ex)
            {
                error = ex.Message;
                return false;
            }
        }

        private static bool ScreenshotExists(string path)
        {
            try { return File.Exists(path) && new FileInfo(path).Length > 0; }
            catch { return false; }
        }

        private static string SafeFileComponent(string value)
        {
            string safe = SafeReportName(value);
            return safe.Length <= 100 ? safe : safe.Substring(0, 100);
        }

        private static void RunFeatureChecks(SmokeReport report)
        {
            bool runtimePathsValid = RuntimePaths.Validate(out string runtimePathDetail);
            report.Check("FEATURE", "BepInEx and Lunaris runtime paths",
                runtimePathsValid, runtimePathDetail);
            report.Check("FEATURE", "Harmony patch classes",
                SkillsPlugin.AppliedPatchClassCount == SkillsPlugin.ExpectedPatchClassCount &&
                SkillsPlugin.FailedPatchClassCount == 0,
                $"applied={SkillsPlugin.AppliedPatchClassCount}; " +
                $"expected={SkillsPlugin.ExpectedPatchClassCount}; failed={SkillsPlugin.FailedPatchClassCount}");
            report.Check("FEATURE", "skill persistence loaded",
                SkillsSaveManager.HasLoaded && SkillsSaveManager.Data.GetAll().Count == 25,
                $"skills={SkillsSaveManager.Data.GetAll().Count}");
            bool inventorySchema =
                SkillsSaveManager.IsInventoryJsonValid(
                    "{\"__InventoryFormat\":2,\"Probe Item\":1}") &&
                SkillsSaveManager.IsInventoryJsonValid(
                    "{\"Legacy Probe Item\":1}") &&
                !SkillsSaveManager.IsInventoryJsonValid(
                    "{\"__InventoryFormat\":3,\"Probe Item\":1}") &&
                !SkillsSaveManager.IsInventoryJsonValid(
                    "{\"__InventoryFormat\":2,}");
            report.Check("FEATURE", "inventory sidecar schema and recovery gate",
                inventorySchema && Items.ItemFactory.HotLoadRecoveryComplete,
                $"loadedFormat={SkillsSaveManager.LoadedInventoryFormatVersion}; " +
                $"recoveryComplete={Items.ItemFactory.HotLoadRecoveryComplete}");
            string bagData = Items.BagSystem.SerializeBags();
            string bagValidationError =
                Items.BagSystem.GetSerializedDataValidationError(bagData);
            report.Check("FEATURE", "bag save schema",
                bagValidationError == null,
                bagValidationError == null ? null :
                    $"{bagValidationError}; data={bagData.Replace(Environment.NewLine, " ")}");
            report.Check("FEATURE", "known recipe save schema",
                SkillsSaveManager.IsRecipesJsonValid(
                    SkillsSaveManager.SerializeRecipesFile()));
            bool simRecipeRoundTrip =
                Skills.SimPlayerTradeskills.ProbeSimRecipeRoundTrip(
                    out string simRecipeError);
            report.Check("FEATURE", "sim recipe persistence round-trip",
                simRecipeRoundTrip, simRecipeError);
            bool simCraftedItems = Items.SimPlayerCraftedItems
                .RunCatalogSelfTest(out string simCraftedItemsDetail);
            report.Check("FEATURE",
                "SimPlayer crafted gear and quantity-backed food/drink",
                simCraftedItems, simCraftedItemsDetail);
            bool craftedOffhands = Items.ItemFactory
                .RunCraftedOffhandSelfTest(out string craftedOffhandDetail);
            report.Check("FEATURE",
                "curated crafted one-handed offhand weapons",
                craftedOffhands, craftedOffhandDetail);
            bool craftedIcons = Items.ItemFactory
                .RunCraftedIconSelfTest(out string craftedIconDetail);
            report.Check("FEATURE",
                "all crafted item icons unique, transparent, and native-sized",
                craftedIcons, craftedIconDetail);
            report.Check("FEATURE", "Skills UI",
                SkillsPlugin.Instance?.GetComponent<SkillsUI>() != null);
            int activeEventSystems =
                UI.NativeEventSystemGate.ActiveCount;
            report.Check("FEATURE", "single native EventSystem",
                activeEventSystems == 1,
                $"active={activeEventSystems}; addonOwned=0");
            MenuBarButton skillsMenuButton =
                SkillsPlugin.Instance?.GetComponent<MenuBarButton>();
            UI.ExtraSettingsMenuBarButton extraSettingsButton =
                SkillsPlugin.Instance?
                    .GetComponent<UI.ExtraSettingsMenuBarButton>();
            report.Check("FEATURE", "native Skills menu-bar button",
                skillsMenuButton?.UsesNativeHudStyleForTesting == true,
                $"icon={skillsMenuButton?.IconSpriteNameForTesting ?? "missing"}; " +
                $"sibling={skillsMenuButton?.SiblingIndexForTesting ?? -1}");
            report.Check("FEATURE", "native Extra Settings menu-bar button",
                extraSettingsButton?.UsesNativeHudStyleForTesting == true &&
                skillsMenuButton != null &&
                extraSettingsButton.SiblingIndexForTesting ==
                    skillsMenuButton.SiblingIndexForTesting + 1,
                $"icon={extraSettingsButton?.IconSpriteNameForTesting ?? "missing"}; " +
                $"sibling={extraSettingsButton?.SiblingIndexForTesting ?? -1}");
            UtilityActionBar utilityBar =
                SkillsPlugin.Instance?.GetComponent<UtilityActionBar>();
            report.Check("FEATURE", "utility skill HUD icons",
                SkillsPlugin.CfgShowUtilityActionButtons != null &&
                utilityBar?.IsBuiltForTesting == true &&
                utilityBar.IconAssetsLoadedForTesting &&
                utilityBar.CooldownOverlaysBuiltForTesting &&
                Mathf.Approximately(UtilityActionBar.IconSizeForTesting, 42f));
            string readyCooldown =
                UtilityActionBar.FormatCooldownTextForTesting(0f);
            string coolingCooldown =
                UtilityActionBar.FormatCooldownTextForTesting(2.1f);
            report.Check("FEATURE", "utility HUD key badges and cooldown overlays",
                readyCooldown.Length == 0 && coolingCooldown == "3" &&
                UtilityActionBar.FormatKeyForDisplay(KeyCode.F9) == "F9" &&
                UtilityActionBar.FormatKeyForDisplay(KeyCode.F10) == "F10" &&
                UtilityActionBar.FormatKeyForDisplay(KeyCode.Semicolon) == ";");
            report.Check("FEATURE", "test-mode runtime",
                SkillsPlugin.Instance?.GetComponent<AddonTestMode>() != null);
            bool logDisplay = Features.LogDisplayOptions.RunSelfTest(
                out string logDisplayDetail);
            report.Check("FEATURE",
                "configurable chat/combat colors and damage-type labels",
                logDisplay, logDisplayDetail);
            bool logSettingsUi = UI.LogDisplaySettingsUI.RunSelfTest(
                out string logSettingsUiDetail);
            report.Check("FEATURE",
                "standalone Extra Settings UI selectors",
                logSettingsUi, logSettingsUiDetail);
            report.Check("FEATURE", "crafting station catalog",
                Stations.CraftingStations.StationMappingCountForTesting >= 6 &&
                Stations.CraftingStations.StationPlacementCountForTesting >= 6,
                $"mappings={Stations.CraftingStations.StationMappingCountForTesting}; " +
                $"placements={Stations.CraftingStations.StationPlacementCountForTesting}");
            report.Check("FEATURE", "recipe scroll vendor catalog",
                Stations.RecipeVendors.RecipeScrollCountForTesting == ExpectedRecipeCount,
                $"scrolls={Stations.RecipeVendors.RecipeScrollCountForTesting}");
            bool recipeScrollsLast =
                Stations.RecipeVendors.ProbeRecipeScrollOrderingForTesting(
                    out string recipeOrderDetail);
            report.Check("FEATURE", "merchant recipe scrolls ordered last",
                recipeScrollsLast, recipeOrderDetail);
            report.Check("FEATURE", "advertised item stats and recipe catalog",
                Items.ItemFactory.LastCatalogValidationErrors == 0 &&
                Items.ItemFactory.LastValidatedRecipeCount == ExpectedRecipeCount,
                $"recipes={Items.ItemFactory.LastValidatedRecipeCount}; " +
                $"errors={Items.ItemFactory.LastCatalogValidationErrors}");
            report.Check("FEATURE", "persistent consumable buff registry",
                Items.ConsumableBuffs.RegisteredBuffCountForTesting > 0 &&
                Items.ConsumableBuffs.RegisteredBuffCountForTesting ==
                    Items.ItemFactory.ConsumableBuffs.Count,
                $"buffs={Items.ConsumableBuffs.RegisteredBuffCountForTesting}; " +
                $"metadata={Items.ItemFactory.ConsumableBuffs.Count}");
            report.Check("FEATURE", "rare forage equipment catalog",
                Skills.ForagingSkill.GetEquipmentNamesForTesting().Count >= 18,
                $"definitions={Skills.ForagingSkill.GetEquipmentNamesForTesting().Count}");
            bool corpseSafety = Features.CorpseRunSystem.RunSafetySelfTest(
                out string corpseSafetyDetail);
            report.Check("FEATURE", "corpse run atomic recovery safeguards",
                corpseSafety, corpseSafetyDetail);
            bool customClassIdentity = Patches.CustomClassIdentity
                .RunSelfTest(out string customClassIdentityDetail);
            report.Check("FEATURE",
                "native Necromancer and Monk class identity guards",
                customClassIdentity, customClassIdentityDetail);
            bool necromancerCatalog = Necromancer.NecromancerCatalog
                .ValidateCatalog(out string necromancerCatalogDetail);
            report.Check("FEATURE", "Necromancer spell, scroll, icon, and VFX catalog",
                necromancerCatalog, necromancerCatalogDetail);
            bool necromancerBalance = Necromancer.NecromancerCatalog
                .ValidateBalance(out string necromancerBalanceDetail);
            report.Check("FEATURE", "Necromancer native-class balance envelope",
                necromancerBalance, necromancerBalanceDetail);
            bool necromancerAscensions = Necromancer.NecromancerAscensions
                .Validate(out string necromancerAscensionDetail);
            report.Check("FEATURE", "Necromancer five-perk Ascension tree",
                necromancerAscensions, necromancerAscensionDetail);
            bool necromancerAuras = Necromancer.NecromancerAuras
                .ValidateCatalog(out string necromancerAuraDetail);
            report.Check("FEATURE", "Necromancer five-tier unique aura line",
                necromancerAuras, necromancerAuraDetail);
            bool necromancerSharedSkills = Necromancer.NecromancerCatalog
                .IsNecromancerPlayer()
                    ? Necromancer.NecromancerSharedSkills.RunInGameUseSelfTest(
                        out string necromancerSharedSkillsDetailLive)
                    : Necromancer.NecromancerSharedSkills.Validate(
                        out necromancerSharedSkillsDetailLive);
            string necromancerSharedSkillsDetail =
                necromancerSharedSkillsDetailLive;
            report.Check("FEATURE",
                "Necromancer shared Arcanist combat-skill books",
                necromancerSharedSkills, necromancerSharedSkillsDetail);
            bool necromancerGear = Necromancer.NecromancerGear
                .ValidateCatalog(out string necromancerGearDetail);
            report.Check("FEATURE",
                "Necromancer native-budget class gear and charm mold",
                necromancerGear, necromancerGearDetail);
            bool necromancerGearAcquisition = Necromancer.NecromancerGear
                .RunAcquisitionSelfTest(
                    out string necromancerGearAcquisitionDetail);
            report.Check("FEATURE",
                "Necromancer themed loot and world-drop mold acquisition",
                necromancerGearAcquisition,
                necromancerGearAcquisitionDetail);
            bool necromancerSpellAcquisition = Necromancer.NecromancerSpellLoot
                .RunAcquisitionSelfTest(
                    out string necromancerSpellAcquisitionDetail);
            report.Check("FEATURE",
                "Necromancer boss and Planar raid spell acquisition",
                necromancerSpellAcquisition,
                necromancerSpellAcquisitionDetail);
            bool starterAuraMerchant = Necromancer.NecromancerAuras
                .ValidateStarterMerchant(out string starterAuraDetail);
            report.Check("FEATURE", "native starter aura merchant injection",
                starterAuraMerchant, starterAuraDetail);
            bool testingNecromancer = Necromancer.NecromancerCatalog
                .IsNecromancerPlayer();
            string necromancerQaDetail;
            bool necromancerQa;
            if (testingNecromancer)
                necromancerQa = IsNecromancerQaCharacter(
                    out necromancerQaDetail);
            else
            {
                necromancerQa = true;
                necromancerQaDetail = "not a Necromancer test character";
            }
            report.Check("FEATURE", "dedicated Necromancer QA identity",
                necromancerQa, necromancerQaDetail);
            bool necromancerStarterWeapons =
                Necromancer.NecromancerCharacterCreation
                    .ValidateStarterWeapons(out string starterWeaponDetail);
            report.Check("FEATURE",
                "Necromancer starter dagger and sword",
                necromancerStarterWeapons, starterWeaponDetail);
            NPC activeNecromancerPet =
                Necromancer.NecromancerPets.ActivePet;
            Necromancer.NecromancerPetMarker activePetMarker =
                activeNecromancerPet?.GetComponent<
                    Necromancer.NecromancerPetMarker>();
            string petMovementDetailLive = null;
            bool petMovementSync = activePetMarker == null ||
                activePetMarker.ValidateMovementSpeed(
                    out petMovementDetailLive);
            string petMovementDetail = activePetMarker == null
                ? "no active bone thrall; runtime synchronizer registered"
                : petMovementDetailLive;
            report.Check("FEATURE",
                "Necromancer pet matches owner movement speed",
                petMovementSync, petMovementDetail);
            bool petGearSchema = Necromancer.NecromancerPetEquipment
                .RunSerializationSelfTest(out string petGearSchemaDetail);
            report.Check("FEATURE", "Necromancer pet gear exact-state persistence",
                petGearSchema, petGearSchemaDetail);
            bool necromancerVendor = Necromancer.NecromancerVendors
                .ValidateCurrentSceneVendor(out string necromancerVendorDetail);
            report.Check("FEATURE", "Necromancer level-sorted city spell and aura vendor",
                necromancerVendor, necromancerVendorDetail);
            CheckCraftedArmorPaletteDiversity(report);

            CheckPatch(report, "inventory save integration", typeof(Inventory), "ToggleInv");
            CheckPatch(report, "fishing progression", typeof(Fishing), "resetFishing");
            CheckPatch(report, "fishing bonus", typeof(Fishing), "Update");
            CheckPatch(report, "swimming progression", typeof(PlayerControl), "WaterMovement");
            CheckPatch(report, "meditation state", typeof(PlayerControl), "LateUpdate");
            CheckPatch(report, "custom consumables and bags", typeof(ItemIcon), "UseConsumable");
            CheckPatch(report, "bag inventory drag-in hook", typeof(ItemIcon), "OnPointerUp");
            report.Check("FEATURE", "bag bidirectional drag-drop handlers",
                typeof(UnityEngine.EventSystems.IBeginDragHandler).IsAssignableFrom(
                    typeof(Items.BagSlotInteraction)) &&
                typeof(UnityEngine.EventSystems.IDragHandler).IsAssignableFrom(
                    typeof(Items.BagSlotInteraction)) &&
                typeof(UnityEngine.EventSystems.IEndDragHandler).IsAssignableFrom(
                    typeof(Items.BagSlotInteraction)) &&
                typeof(UnityEngine.EventSystems.IPointerClickHandler).IsAssignableFrom(
                    typeof(Items.BagSlotInteraction)));
            CheckPatch(report, "food and magic buff duration", typeof(Stats), "AddStatusEffect");
            CheckPatch(report, "melee combat skill", typeof(PlayerCombat), "PerformAttacks");
            CheckPatch(report, "wand combat skill", typeof(PlayerCombat), "DoWandAttack");
            CheckPatch(report, "archery combat skill", typeof(PlayerCombat), "DoBowAttack");
            CheckPatch(report, "sim melee synchronization", typeof(NPC), "PerformMeleeHit");
            CheckPatch(report, "chat commands", typeof(TypeText), "CheckCommands");
            CheckPatch(report, "chat and combat log color routing",
                typeof(ChatLogLine), ".ctor");
            CheckPatch(report, "resolved damage-type capture",
                typeof(Stats), "ReduceHP");
            CheckPatch(report, "corpse run death capture", typeof(Character), "DoDeath");
            CheckPatch(report, "corpse run native save result", typeof(GameData), "SaveData");
            CheckPatch(report, "corpse run loot-key priority", typeof(PlayerCombat), "FindNearestLootable");
            CheckPatch(report, "smithing station", typeof(Smithing), "OpenWindow");
            CheckPatch(report, "zone crafting stations", typeof(GameManager), "ShowZoneName");
            CheckPatch(report, "recipe vendors", typeof(VendorWindow), "LoadWindow");
            CheckPatch(report, "custom item database", typeof(ItemDatabase), "GetItemByID");
            CheckPatch(report, "persistent custom buffs", typeof(SpellDB), "GetSpellByID");
            CheckPatch(report, "recipe, meat, and endgame loot", typeof(LootTable), "LoadLootTable");
            CheckPatch(report, "mining material drops", typeof(MiningNode), "Mine");
            CheckPatch(report, "magic cast progression", typeof(CastSpell), "StartSpell");
            CheckPatch(report, "spell damage bonuses", typeof(SpellVessel), "ResolveSpell");
            CheckPatch(report, "summoned-pet physical damage bonus", typeof(Character), "DamageMe");
            CheckPatch(report, "summoned-pet spell damage bonus", typeof(Character), "MagicDamageMe");
            CheckPatch(report, "sim WTB chat tracking", typeof(UpdateSocialLog), "LogAdd");
            CheckPatch(report, "sim recipe trading", typeof(SimTradeWindow), "CompleteTrade");
            CheckPatch(report, "Necromancer character creation", typeof(CharSelectManager), "SaveChar");
            CheckPatch(report, "custom class native-load recognition",
                typeof(CharSelectManager), "SelectSlot");
            CheckPatch(report, "custom class enter-world recognition",
                typeof(CharSelectManager), "Play");
            CheckPatch(report, "custom class native-save protection",
                typeof(GameManager), "SaveGameData");
            CheckPatch(report, "Necromancer starter aura merchant",
                typeof(VendorInventory), "Start");
            CheckPatch(report, "Necromancer class gear loot injection",
                typeof(LootTable), "Start");
            CheckPatch(report, "Necromancer pet equipment trade", typeof(TradeWindow), "CompleteTrade");
            CheckPatch(report, "Necromancer feign-death aggro guard", typeof(NPC), "ManageAggro");

            bool utilityMath = IsFiniteNonNegative(Skills.FishingSkill.CatchBonus) &&
                IsFinitePositive(Skills.SwimmingSkill.SpeedMultiplier) &&
                IsFinitePositive(Skills.BindWoundSkill.Cooldown) &&
                IsFiniteNonNegative(Skills.ForagingSkill.CooldownRemaining) &&
                IsFiniteNonNegative(Skills.BindWoundSkill.CooldownRemaining) &&
                IsFiniteNonNegative(Skills.BeggingSkill.CooldownRemaining) &&
                Skills.BindWoundSkill.CalculateHeal(1000) > 0 &&
                IsFiniteNonNegative(Skills.MeditateSkill.BonusManaPerTick) &&
                IsFiniteNonNegative(Skills.FoodToleranceSkill.DurationBonus);
            report.Check("FEATURE", "utility skill calculations", utilityMath);

            bool combatMath = true;
            foreach (Skills.CombatSkills.WeaponType type in Enum.GetValues(
                typeof(Skills.CombatSkills.WeaponType)))
            {
                if (type == Skills.CombatSkills.WeaponType.Unknown) continue;
                combatMath &= IsFinitePositive(
                    Skills.CombatSkills.GetDamageMultiplier(type));
            }
            combatMath &= Skills.CombatSkills.ClassifyWeapon(
                "Test Shield", false) == Skills.CombatSkills.WeaponType.Unknown;
            combatMath &= Skills.CombatSkills.ClassifyWeapon(
                "Test Wand", false) == Skills.CombatSkills.WeaponType.Wands;
            combatMath &= Skills.CombatSkills.ClassifyWeapon(
                "Novice Handwraps", false) ==
                    Skills.CombatSkills.WeaponType.HandToHand;
            combatMath &= Skills.CombatSkills.ClassifyWeapon(
                "Runed Knuckles", false) ==
                    Skills.CombatSkills.WeaponType.HandToHand;
            report.Check("FEATURE", "all eight combat skill calculations", combatMath);

            bool magicMath = IsFinitePositive(Skills.MagicSkills.GetSpellDamageMultiplier()) &&
                IsFinitePositive(Skills.MagicSkills.GetHealMultiplier()) &&
                IsFinitePositive(Skills.MagicSkills.GetBuffDurationMultiplier()) &&
                IsFinitePositive(Skills.MagicSkills.GetConjurationMultiplier()) &&
                Skills.MagicSkills.ScalePositiveDamage(100, 1.144f) == 114 &&
                Skills.MagicSkills.ScalePositiveDamage(100, 0.20f) == 20 &&
                Skills.MagicSkills.ScalePositiveDamage(0, 1.144f) == 0 &&
                Skills.MagicSkills.ScalePositiveDamage(-1, 1.144f) == -1;
            report.Check("FEATURE", "all four magic skill calculations", magicMath);
            string schoolOnce = Patches.Patch_SpellTooltip.AppendSchoolTagOnce(
                "Spell details", Skills.MagicSkills.MagicSchool.Abjuration);
            string schoolTwice = Patches.Patch_SpellTooltip.AppendSchoolTagOnce(
                schoolOnce, Skills.MagicSkills.MagicSchool.Abjuration);
            report.Check("FEATURE", "magic-school tooltip tag is idempotent",
                string.Equals(schoolOnce, schoolTwice, StringComparison.Ordinal) &&
                schoolOnce.IndexOf("School: Abjuration", StringComparison.Ordinal) >= 0);
            string schoolRecolored =
                "Spell details\n<color=#FFFFFF>School: Abjuration</color>";
            report.Check("FEATURE", "magic-school tooltip survives markup rebuilds",
                string.Equals(schoolRecolored,
                    Patches.Patch_SpellTooltip.AppendSchoolTagOnce(
                        schoolRecolored,
                        Skills.MagicSkills.MagicSchool.Abjuration),
                    StringComparison.Ordinal));
            string weaponOnce = Patches.Patch_TooltipInject.AppendVisibleLineOnce(
                "Weapon lore", "Skill: Piercing",
                "<color=#FF9800>Skill: Piercing</color>");
            string weaponTwice = Patches.Patch_TooltipInject.AppendVisibleLineOnce(
                weaponOnce, "Skill: Piercing",
                "<color=#FF9800>Skill: Piercing</color>");
            report.Check("FEATURE", "weapon-skill tooltip tag is idempotent",
                string.Equals(weaponOnce, weaponTwice, StringComparison.Ordinal) &&
                Patches.Patch_TooltipInject.ContainsVisibleLine(
                    weaponTwice, "Skill: Piercing"));
            Item foodTooltipItem = Items.ItemFactory.GetItem("Campfire Ash Cake");
            report.Check("FEATURE", "consumables excluded from magic-school tooltip",
                foodTooltipItem?.ItemEffectOnClick != null &&
                !Patches.Patch_SpellTooltip.ShouldAnnotate(
                    foodTooltipItem.ItemEffectOnClick));
            report.Check("FEATURE", "sim-player virtual skills",
                Skills.SimPlayerSkills.GetVirtualSkillLevel(1) == 10 &&
                Skills.SimPlayerSkills.GetVirtualSkillLevel(35) == 180);
            report.Check("FEATURE", "sim crafting, WTB, and WTS config",
                SkillsPlugin.CfgEnableSimCraft != null &&
                SkillsPlugin.CfgEnableSimWTB != null &&
                SkillsPlugin.CfgEnableSimWTS != null);
        }

        private static List<string> ValidateItem(CatalogEntry entry,
            HashSet<string> duplicateIds)
        {
            var errors = new List<string>();
            Item item = entry?.Item;
            if (item == null)
            {
                errors.Add("null item");
                return errors;
            }
            if (string.IsNullOrWhiteSpace(item.ItemName) ||
                !string.Equals(item.ItemName, entry.Name, StringComparison.Ordinal))
                errors.Add("registry name mismatch");
            if (string.IsNullOrWhiteSpace(item.Id)) errors.Add("missing persistent ID");
            else if (duplicateIds.Contains(item.Id)) errors.Add("duplicate persistent ID");
            if (item.ItemValue < 0) errors.Add("negative item value");
            if (item.ItemLevel < 0 || item.ItemLevel > 1000) errors.Add("invalid item level");
            if (item.Classes == null) errors.Add("null class list");
            if (item.ItemIcon == null) errors.Add("missing inventory icon");
            if (item.Stackable && item.RequiredSlot != Item.SlotType.General)
                errors.Add("equippable item is stackable");
            if (item.RequiredSlot != Item.SlotType.General)
                ValidateVisualPalette(item, errors);

            if (!string.IsNullOrWhiteSpace(item.Id))
            {
                Item byId = Items.ItemFactory.GetItemById(item.Id);
                if (byId == null || !string.Equals(byId.ItemName, item.ItemName,
                    StringComparison.Ordinal))
                    errors.Add("ID lookup failed");
            }
            Item byName = Items.ItemFactory.ResolveItemForTesting(item.ItemName);
            if (byName == null || !string.Equals(byName.Id ?? byName.ItemName,
                item.Id ?? item.ItemName, StringComparison.OrdinalIgnoreCase))
                errors.Add("name lookup failed");

            bool weapon = item.ThisWeaponType != Item.WeaponType.None ||
                item.IsBow || item.IsWand;
            if (weapon && (item.WeaponDmg <= 0 || item.WeaponDly <= 0f))
                errors.Add("weapon has invalid damage or delay");
            if (item.IsBow && (item.BowRange <= 0 || item.BowArrowSpeed <= 0f))
                errors.Add("bow has invalid range or arrow speed");

            if (Items.BagSystem.IsBag(item) &&
                (item.Stackable || item.Disposable || !item.NoTradeNoDestroy ||
                 !item.PlayerCannotSell || item.ItemEffectOnClick == null ||
                 Items.BagSystem.GetBagSlots(item.ItemName) <= 0))
                errors.Add("bag safety contract failed");

            // ItemIcon.QuickSell rejects any non-positive value or either
            // restriction flag. QuickBank has no per-item restriction, while
            // trash uses NoTradeNoDestroy. Check every catalog entry so an
            // unsellable/untrashable custom item cannot ship unnoticed.
            bool physicalBag = Items.BagSystem.IsBag(item);
            bool corpseBag = physicalBag &&
                Items.BagSystem.IsCorpseBag(item.ItemName);
            if (!physicalBag && item.ItemValue <= 0)
                errors.Add("not sellable: native vendor requires positive value");
            if (!physicalBag && item.PlayerCannotSell)
                errors.Add("not sellable: PlayerCannotSell is set");
            if (!physicalBag && item.NoTradeNoDestroy)
                errors.Add("not trashable: NoTradeNoDestroy is set");
            if (corpseBag && !item.NoTradeNoDestroy)
                errors.Add("corpse recovery bag lost its transfer guard");

            if (Stations.RecipeVendors.IsRecipeScroll(item) &&
                (item.Stackable || item.PlayerCannotSell ||
                 item.ItemEffectOnClick == null ||
                 item.RequiredSlot != Item.SlotType.General))
                errors.Add("recipe scroll contract failed");

            if (Items.ItemFactory.ConsumableBuffs.ContainsKey(item.ItemName))
            {
                Spell spell = item.ItemEffectOnClick;
                if (!item.Disposable || !item.Stackable || spell == null)
                    errors.Add("consumable activation contract failed");
                else if (string.IsNullOrWhiteSpace(spell.Id) ||
                    spell.SpellDurationInTicks <= 0 ||
                    !ReferenceEquals(Items.ConsumableBuffs.GetBuffSpellById(spell.Id), spell))
                    errors.Add("persistent consumable buff lookup failed");
            }
            return errors;
        }

        private static void ValidateVisualPalette(Item item,
            List<string> errors)
        {
            Color[] colors =
            {
                item.ItemPrimaryColor, item.ItemSecondaryColor,
                item.ItemMetalPrimary, item.ItemMetalSecondary,
                item.ItemLeatherPrimary, item.ItemLeatherSecondary
            };
            for (int i = 0; i < colors.Length; i++)
            {
                Color color = colors[i];
                if (!IsFiniteUnit(color.r) || !IsFiniteUnit(color.g) ||
                    !IsFiniteUnit(color.b) || !IsFiniteUnit(color.a))
                {
                    errors.Add("visual palette contains a non-finite or out-of-range channel");
                    return;
                }
            }

            bool identical = true;
            for (int i = 1; i < colors.Length; i++)
                identical &= ColorDistance(colors[0], colors[i]) < 0.015f;
            Color first = colors[0];
            bool hotPink = first.r > 0.55f && first.b > 0.35f &&
                first.r - first.g > 0.22f && first.b - first.g > 0.10f;
            if (identical && hotPink)
                errors.Add("all material channels repeat a saturated pink tint");
        }

        private static void CheckCraftedArmorPaletteDiversity(
            SmokeReport report)
        {
            var signatures = new Dictionary<string, int>(
                StringComparer.Ordinal);
            int armorCount = 0;
            foreach (Item item in Items.ItemFactory.GetRegisteredItemsForTesting())
            {
                if (item == null || !IsRenderedArmorSlot(item.RequiredSlot))
                    continue;
                armorCount++;
                string signature = PaletteSignature(item);
                signatures.TryGetValue(signature, out int count);
                signatures[signature] = count + 1;
            }
            int largestGroup = 0;
            foreach (int count in signatures.Values)
                largestGroup = Mathf.Max(largestGroup, count);
            int allowedLargestGroup = Mathf.Max(2,
                Mathf.CeilToInt(armorCount * 0.35f));
            bool varied = armorCount > 0 && signatures.Count >= 8 &&
                largestGroup <= allowedLargestGroup;
            report.Check("FEATURE", "crafted armor palette diversity", varied,
                $"armor={armorCount}; palettes={signatures.Count}; " +
                $"largestGroup={largestGroup}; allowed={allowedLargestGroup}");
        }

        private static bool IsRenderedArmorSlot(Item.SlotType slot)
        {
            switch (slot)
            {
                case Item.SlotType.Head:
                case Item.SlotType.Chest:
                case Item.SlotType.Shoulder:
                case Item.SlotType.Arm:
                case Item.SlotType.Bracer:
                case Item.SlotType.Hand:
                case Item.SlotType.Foot:
                case Item.SlotType.Leg:
                case Item.SlotType.Back:
                    return true;
                default:
                    return false;
            }
        }

        private static string PaletteSignature(Item item)
        {
            Color[] colors =
            {
                item.ItemPrimaryColor, item.ItemSecondaryColor,
                item.ItemMetalPrimary, item.ItemMetalSecondary,
                item.ItemLeatherPrimary, item.ItemLeatherSecondary
            };
            var signature = new StringBuilder();
            foreach (Color color in colors)
            {
                signature.Append(Mathf.RoundToInt(Mathf.Clamp01(color.r) * 15f))
                    .Append(Mathf.RoundToInt(Mathf.Clamp01(color.g) * 15f))
                    .Append(Mathf.RoundToInt(Mathf.Clamp01(color.b) * 15f))
                    .Append('|');
            }
            return signature.ToString();
        }

        private static float ColorDistance(Color left, Color right)
        {
            return Mathf.Abs(left.r - right.r) +
                Mathf.Abs(left.g - right.g) +
                Mathf.Abs(left.b - right.b);
        }

        private static bool IsFiniteUnit(float value)
        {
            return !float.IsNaN(value) && !float.IsInfinity(value) &&
                value >= 0f && value <= 1f;
        }

        private static int RunRecipeChecks(SmokeReport report)
        {
            int count = 0;
            var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (string tradeskill in TradeskillNames)
            {
                foreach (var recipe in Skills.Tradeskills.GetRecipesForSkillPublic(tradeskill))
                {
                    count++;
                    var errors = new List<string>();
                    if (recipe == null || string.IsNullOrWhiteSpace(recipe.Name) ||
                        !names.Add(recipe?.Name ?? ""))
                    {
                        report.Check("RECIPE", recipe?.Name ?? "<null>", false,
                            "null, unnamed, or duplicate recipe");
                        continue;
                    }
                    if (!string.Equals(recipe.Tradeskill, tradeskill,
                        StringComparison.Ordinal)) errors.Add("tradeskill mismatch");
                    if (recipe.MinSkillLevel < 1 || recipe.MinSkillLevel > 180 ||
                        recipe.TrivialLevel < recipe.MinSkillLevel ||
                        recipe.TrivialLevel > 180) errors.Add("invalid level range");
                    if (recipe.Materials == null || recipe.MaterialCounts == null ||
                        recipe.Materials.Length == 0 ||
                        recipe.Materials.Length != recipe.MaterialCounts.Length)
                        errors.Add("invalid material arrays");
                    else
                    {
                        for (int i = 0; i < recipe.Materials.Length; i++)
                            if (string.IsNullOrWhiteSpace(recipe.Materials[i]) ||
                                recipe.MaterialCounts[i] <= 0 ||
                                Items.ItemFactory.ResolveItem(recipe.Materials[i]) == null)
                                errors.Add($"unresolved material {recipe.Materials[i]}");
                    }
                    Item output = Items.ItemFactory.GetItem(recipe.Name);
                    if (output == null) errors.Add("missing output item");
                    Item scroll = Stations.RecipeVendors.GetScrollForRecipe(recipe.Name);
                    if (scroll == null || !Stations.RecipeVendors.IsRecipeScroll(scroll))
                        errors.Add("missing recipe scroll");
                    if (recipe.MinSkillLevel > 90 && !recipe.DropOnly)
                        errors.Add("endgame recipe is not drop-only");

                    var low = new SkillEntry { Name = tradeskill,
                        Level = recipe.MinSkillLevel };
                    var high = new SkillEntry { Name = tradeskill, Level = 180 };
                    float lowChance = Skills.Tradeskills.GetSuccessChance(low, recipe);
                    float highChance = Skills.Tradeskills.GetSuccessChance(high, recipe);
                    if (!IsFinitePositive(lowChance) || lowChance < 15f ||
                        lowChance > 95f || highChance < lowChance || highChance > 95f)
                        errors.Add("invalid success chance curve");

                    report.Check("RECIPE", recipe.Name, errors.Count == 0,
                        errors.Count == 0
                            ? $"{tradeskill}; materials={recipe.Materials.Length}; " +
                              $"chance={lowChance:F1}-{highChance:F1}%"
                            : string.Join("; ", errors));
                }
            }
            return count;
        }

        private static List<CatalogEntry> BuildCatalog(bool includeNative)
        {
            if (Items.ItemFactory.Initialized)
                Stations.RecipeVendors.Initialize();
            var byName = new Dictionary<string, CatalogEntry>(
                StringComparer.OrdinalIgnoreCase);
            foreach (Item item in Items.ItemFactory.GetRegisteredItemsForTesting())
            {
                string source = Stations.RecipeVendors.IsRecipeScroll(item)
                    ? "recipe-scroll" : "addon";
                byName[item.ItemName] = new CatalogEntry
                {
                    Name = item.ItemName, Item = item, Source = source
                };
            }
            foreach (string name in Skills.ForagingSkill.GetEquipmentNamesForTesting())
            {
                if (byName.ContainsKey(name)) continue;
                Item item = Items.ItemFactory.ResolveItemForTesting(name);
                if (item != null)
                    byName[name] = new CatalogEntry
                    {
                        Name = name, Item = item, Source = "forage-equipment"
                    };
            }
            if (includeNative)
            {
                foreach (Item item in Items.ItemFactory.GetNativeItemsForTesting())
                {
                    if (byName.ContainsKey(item.ItemName)) continue;
                    byName[item.ItemName] = new CatalogEntry
                    {
                        Name = item.ItemName, Item = item, Source = "native"
                    };
                }
            }
            var result = new List<CatalogEntry>(byName.Values);
            result.Sort((left, right) => string.Compare(left.Name, right.Name,
                StringComparison.OrdinalIgnoreCase));
            return result;
        }

        private static List<CatalogEntry> GetEquippableGear(
            List<CatalogEntry> catalog)
        {
            var gear = new List<CatalogEntry>();
            foreach (CatalogEntry entry in catalog)
                if (entry?.Item != null &&
                    entry.Item.RequiredSlot != Item.SlotType.General)
                    gear.Add(entry);
            gear.Sort((left, right) =>
            {
                int slot = left.Item.RequiredSlot.CompareTo(
                    right.Item.RequiredSlot);
                return slot != 0 ? slot : string.Compare(left.Name,
                    right.Name, StringComparison.OrdinalIgnoreCase);
            });
            return gear;
        }

        private static List<CatalogEntry> FindCatalogMatches(string query,
            bool includeNative)
        {
            var result = new List<CatalogEntry>();
            foreach (var entry in BuildCatalog(includeNative))
                if (entry.Name.IndexOf(query ?? "",
                    StringComparison.OrdinalIgnoreCase) >= 0)
                    result.Add(entry);
            return result;
        }

        private static CatalogEntry ResolveUniqueCatalogMatch(string query,
            bool includeNative)
        {
            var matches = FindCatalogMatches((query ?? "").Trim(), includeNative);
            CatalogEntry exact = null;
            foreach (var match in matches)
                if (string.Equals(match.Name, query,
                    StringComparison.OrdinalIgnoreCase))
                {
                    exact = match;
                    break;
                }
            if (exact != null) return exact;
            if (matches.Count == 1) return matches[0];
            if (matches.Count == 0)
            {
                ChatHelper.Send(
                    $"<color=#EF5350>[Test Mode]</color> No item matches '{query}'.");
                return null;
            }
            ChatHelper.Send(
                $"<color=#EF5350>[Test Mode]</color> '{query}' is ambiguous ({matches.Count} matches):");
            for (int i = 0; i < Mathf.Min(10, matches.Count); i++)
                ChatHelper.Send($"  <color=#FFFFFF>{matches[i].Name}</color>");
            return null;
        }

        private static Skills.Tradeskills.Recipe ResolveUniqueRecipe(string query)
        {
            query = (query ?? "").Trim();
            var matches = new List<Skills.Tradeskills.Recipe>();
            Skills.Tradeskills.Recipe exact = null;
            foreach (string tradeskill in TradeskillNames)
            {
                foreach (var recipe in Skills.Tradeskills.GetRecipesForSkillPublic(tradeskill))
                {
                    if (recipe.Name.IndexOf(query,
                        StringComparison.OrdinalIgnoreCase) < 0) continue;
                    matches.Add(recipe);
                    if (string.Equals(recipe.Name, query,
                        StringComparison.OrdinalIgnoreCase)) exact = recipe;
                }
            }
            if (exact != null) return exact;
            if (matches.Count == 1) return matches[0];
            if (matches.Count == 0)
                ChatHelper.Send(
                    $"<color=#EF5350>[Test Mode]</color> No recipe matches '{query}'.");
            else
            {
                ChatHelper.Send(
                    $"<color=#EF5350>[Test Mode]</color> Recipe name is ambiguous ({matches.Count} matches):");
                for (int i = 0; i < Mathf.Min(10, matches.Count); i++)
                    ChatHelper.Send($"  <color=#FFFFFF>{matches[i].Name}</color>");
            }
            return null;
        }

        private static SkillEntry ResolveUniqueSkill(string query)
        {
            var matches = new List<SkillEntry>();
            SkillEntry exact = null;
            foreach (var skill in SkillsSaveManager.Data.GetAll())
            {
                if ((skill.Name ?? "").IndexOf(query,
                    StringComparison.OrdinalIgnoreCase) < 0) continue;
                matches.Add(skill);
                if (string.Equals(skill.Name, query,
                    StringComparison.OrdinalIgnoreCase)) exact = skill;
            }
            if (exact != null) return exact;
            if (matches.Count == 1) return matches[0];
            ChatHelper.Send(matches.Count == 0
                ? $"<color=#EF5350>[Test Mode]</color> No skill matches '{query}'."
                : $"<color=#EF5350>[Test Mode]</color> Skill name is ambiguous ({matches.Count} matches).");
            return null;
        }

        private static HashSet<string> FindDuplicateIds(List<CatalogEntry> catalog)
        {
            var first = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var duplicates = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var entry in catalog)
                if (!string.IsNullOrWhiteSpace(entry.Item?.Id) &&
                    !first.Add(entry.Item.Id)) duplicates.Add(entry.Item.Id);
            return duplicates;
        }

        private static void CheckPatch(SmokeReport report, string feature,
            Type targetType, string methodName)
        {
            bool found = false;
            foreach (MethodBase method in Harmony.GetAllPatchedMethods())
            {
                if (method?.DeclaringType != targetType ||
                    !string.Equals(method.Name, methodName,
                        StringComparison.Ordinal)) continue;
                HarmonyLib.Patches info = Harmony.GetPatchInfo(method);
                if (HasOwner(info?.Prefixes) || HasOwner(info?.Postfixes) ||
                    HasOwner(info?.Transpilers) || HasOwner(info?.Finalizers))
                {
                    found = true;
                    break;
                }
            }
            report.Check("FEATURE", feature, found,
                $"target={targetType.Name}.{methodName}");
        }

        private static bool HasOwner(IEnumerable<Patch> patches)
        {
            if (patches == null) return false;
            foreach (Patch patch in patches)
                if (patch != null && string.Equals(patch.owner,
                    SkillsPlugin.PluginGuid, StringComparison.Ordinal)) return true;
            return false;
        }

        private static bool RequireLoadedWorld()
        {
            if (SkillsSaveManager.HasLoaded && GameData.PlayerInv?.StoredSlots != null)
                return true;
            ChatHelper.Send(
                "<color=#EF5350>[Test Mode]</color> Load a character into the game world first.");
            return false;
        }

        private static bool IsNecromancerQaCharacter(out string detail)
        {
            string current = GameData.PlayerStats?.MyName;
            if (string.IsNullOrWhiteSpace(current))
                current = SkillsSaveManager.CharName;
            bool valid =
                string.Equals(current, NecromancerQaCharacterName,
                    StringComparison.Ordinal) ||
                string.Equals(current, NecromancerQaCharacterAlias,
                    StringComparison.Ordinal);
            detail = valid
                ? $"dedicated QA character={current}"
                : $"current character='{current ?? "unavailable"}', expected='{NecromancerQaCharacterName}' or '{NecromancerQaCharacterAlias}'";
            return valid;
        }

        private static bool RequireNecromancerQaCharacter()
        {
            if (IsNecromancerQaCharacter(out string detail)) return true;
            ChatHelper.Send(
                "<color=#EF5350>[Test Mode]</color> Necromancer QA commands are locked to the dedicated John EverQuest or John Necromancer saves so normal saves cannot be changed. " +
                detail);
            return false;
        }

        private static bool RequireActiveNecromancerForReadOnlyQa()
        {
            if (Necromancer.NecromancerCatalog.IsNecromancerPlayer())
                return true;
            ChatHelper.Send(
                "<color=#EF5350>[Test Mode]</color> This read-only Necromancer QA command requires an active Necromancer character.");
            return false;
        }

        private static void ParseNameAndQuantity(string arguments,
            out string name, out int amount)
        {
            name = (arguments ?? "").Trim();
            amount = 1;
            int separator = name.LastIndexOf(' ');
            if (separator > 0 && int.TryParse(name.Substring(separator + 1),
                out int parsed))
            {
                amount = parsed;
                name = name.Substring(0, separator).Trim();
            }
        }

        private static string TakeFirstToken(ref string value)
        {
            value = (value ?? "").Trim();
            int space = value.IndexOf(' ');
            if (space < 0)
            {
                string token = value;
                value = "";
                return token;
            }
            string result = value.Substring(0, space);
            value = value.Substring(space + 1).Trim();
            return result;
        }

        private static string SafeReportName(string value)
        {
            if (string.IsNullOrWhiteSpace(value)) return "unknown";
            var sb = new StringBuilder();
            foreach (char c in value)
                sb.Append(char.IsLetterOrDigit(c) || c == '-' || c == '_'
                    ? c : '_');
            return sb.ToString();
        }

        private static bool IsFinitePositive(float value)
        {
            return !float.IsNaN(value) && !float.IsInfinity(value) && value > 0f;
        }

        private static bool IsFiniteNonNegative(float value)
        {
            return !float.IsNaN(value) && !float.IsInfinity(value) && value >= 0f;
        }
    }
}
