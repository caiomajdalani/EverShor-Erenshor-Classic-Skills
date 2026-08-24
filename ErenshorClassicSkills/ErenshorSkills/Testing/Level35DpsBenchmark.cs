using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using HarmonyLib;
using UnityEngine;
using UnityEngine.AI;

namespace ErenshorSkills.Testing
{
    /// <summary>
    /// Repeatable, save-guarded progression and level-35 single-target parses.
    /// Every hit is
    /// resolved by Erenshor: autoattacks, skills, projectiles, spells, resists,
    /// DoT ticks, criticals, Ascensions, and pets all damage a live native
    /// training-dummy prefab. The harness only chooses actions and records the
    /// dummy's actual HP delta.
    /// </summary>
    internal static class Level35DpsBenchmark
    {
        private const int MaximumLevel = 35;
        private const int DummyHealth = 500000000;
        private const float FullTimeScale = 1f;
        private const float AcceleratedAuditTimeScale = 5f;
        private const float QuickTimeScale = 12f;
        private const int BenchmarkTargetFrameRate = 240;
        private const float MinimumCadenceScaleRatio = 0.80f;
        private const float MinimumAcceleratedFramesPerSecond = 2.5f;
        private const int MaximumActionReplaysPerFrame = 8;
        private const int MaximumCadenceAttempts = 4;
        private const float MinimumPeerRatio = 0.85f;
        private const float MaximumPeerRatio = 1.15f;
        private const float MaximumRepetitionSpread = 0.35f;
        private const float MaximumMonkRepetitionSpread = 0.20f;
        private const float MaximumTimeScaleBias = 0.10f;
        private static readonly int[] ProgressionLevels =
            { 5, 10, 15, 20, 25, 30, 35 };
        private static int _activeLevel = MaximumLevel;
        private static GearPower _activeGearPower = GearPower.Optimized;
        private static int _standardBaseHp;
        private static int _standardBaseMana;

        // Native NPC armor is exactly level * 15. Native same-level NPC
        // resistance is randomized from 0.5x to 1.2x level in Stats.Start();
        // use its deterministic midpoint so paired caster parses are stable.
        private static int ActiveDummyArmor => _activeLevel * 15;
        private static int ActiveDummyResist =>
            Mathf.RoundToInt(_activeLevel * 0.85f);

        private enum WeaponProfile
        {
            Wand,
            DualWield,
            TwoHandMelee,
            Bow
        }

        private enum GearPower
        {
            Budget,
            Progression,
            Optimized
        }

        private static int ActiveGearLevelCeiling
        {
            get
            {
                switch (_activeGearPower)
                {
                    case GearPower.Budget:
                        return Math.Max(1,
                            Mathf.RoundToInt(_activeLevel * 0.55f));
                    case GearPower.Progression:
                        return Math.Max(1,
                            Mathf.RoundToInt(_activeLevel * 0.80f));
                    default:
                        return _activeLevel;
                }
            }
        }

        private sealed class ClassProfile
        {
            internal string Name;
            internal Class Class;
            internal WeaponProfile Weapon;
            internal bool IsNecromancer;
            internal bool IsMonk;
        }

        private sealed class ParseSpec
        {
            internal ClassProfile Profile;
            internal int Level;
            internal bool MaxAscension;
            internal int DurationSeconds;
            internal float TimeScale;
            internal int Seed;
            internal int Repetition;
            internal GearPower GearPower;
            internal bool Sustained => DurationSeconds > 60;
        }

        private sealed class ParseResult
        {
            internal string ClassName;
            internal int Level;
            internal string Scenario;
            internal int DurationSeconds;
            internal float TimeScale;
            internal int SimulationFrames;
            internal int ActionOpportunities;
            internal int SuccessfulActions;
            internal double WallDurationSeconds;
            internal int Seed;
            internal int Repetition;
            internal string GearPower;
            internal int GearLevelCeiling;
            internal long Damage;
            internal double Dps;
            internal long WeaponDamage;
            internal long SkillDamage;
            internal long DirectSpellDamage;
            internal long AreaSpellDamage;
            internal long DotDamage;
            internal long PetDamage;
            internal long OtherDamage;
            internal int StartingMana;
            internal int EndingMana;
            internal int StartingHealth;
            internal int MinimumHealth;
            internal int EndingHealth;
            internal bool PlayerAlive;
            internal string Weapon;
            internal string Loadout;
            internal string Stats;
            internal string ActiveEffects;
            internal string Pet;
            internal string PetWeapon;
            internal int PetSummonCost;
            internal int PetSummonMaximumMana;
            internal bool PetSummonAffordable;
            internal readonly Dictionary<string, int> Spells =
                new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            internal readonly Dictionary<string, int> Skills =
                new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            internal readonly Dictionary<string, long> DamageSources =
                new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase);
        }

        private sealed class BenchmarkOptions
        {
            internal bool Quick;
            internal int[] Levels;
            internal string Slug;
            internal string DisplayName;
            internal string[] ProfileNames;
            internal int[] Durations;
            internal int Repetitions;
            internal float[] TimeScales;
            internal GearPower[] GearPowers;
            internal bool DiagnosticOnly;
            internal bool IncludeMaxAscension;
            internal bool IncludeMonk;
            internal bool MonkPeerGuard;
            internal bool TimeScaleGuard;
            internal string ReportCategory;
            internal string ReportFileName;
            internal bool IsProgression => Levels != null && Levels.Length > 1;
        }

        private sealed class ParseCadenceException :
            InvalidOperationException
        {
            internal ParseCadenceException(string message) : base(message)
            {
            }
        }

        private sealed class StatusState
        {
            internal Spell Effect;
            internal float Duration;
            internal bool FromPlayer;
            internal int BonusDamage;
            internal bool CastedByPc;
            internal Character Owner;
            internal Character CreditDps;
        }

        private sealed class InventoryState
        {
            internal readonly List<EquipmentSlotState> Slots =
                new List<EquipmentSlotState>();
            internal Item MainHand;
            internal int MainHandQuantity;
            internal Item OffHand;
            internal int OffHandQuantity;
            internal int ItemHp;
            internal int ItemAc;
            internal int ItemMana;
            internal int ItemStr;
            internal int ItemEnd;
            internal int ItemDex;
            internal int ItemAgi;
            internal int ItemInt;
            internal int ItemWis;
            internal int ItemCha;
            internal int ItemRes;
            internal int ItemMr;
            internal int ItemEr;
            internal int ItemPr;
            internal int ItemVr;
            internal float MainHandDelay;
            internal int MainHandDamage;
            internal int OffHandDamage;
            internal float OffHandDelay;
            internal int MainHandLevel;
            internal bool SecondaryWeapon;
            internal bool PrimaryWeapon;
            internal bool TwoHandPrimary;
            internal bool SecondaryShield;
            internal bool PrimaryBow;
            internal int StrScaleMod;
            internal int EndScaleMod;
            internal int DexScaleMod;
            internal int AgiScaleMod;
            internal int IntScaleMod;
            internal int WisScaleMod;
            internal int ChaScaleMod;
            internal float MitScaleMod;
            internal float RstScaleMod;
            internal readonly List<Spell> WornEffects = new List<Spell>();
        }

        private sealed class EquipmentSlotState
        {
            internal ItemIcon Slot;
            internal Item Item;
            internal int Quantity;
        }

        private sealed class PlayerState
        {
            internal Class CharacterClass;
            internal int Level;
            internal int CurrentExperience;
            internal int CurrentAscensionXp;
            internal int BaseHp;
            internal int BaseAc;
            internal int BaseMana;
            internal int BaseStr;
            internal int BaseEnd;
            internal int BaseDex;
            internal int BaseAgi;
            internal int BaseInt;
            internal int BaseWis;
            internal int BaseCha;
            internal int BaseRes;
            internal int BaseMr;
            internal int BaseEr;
            internal int BasePr;
            internal int BaseVr;
            internal int BaseAttackRoll;
            internal float BaseLifesteal;
            internal float BaseMainDelay;
            internal float BaseOffDelay;
            internal float BaseArmorPen;
            internal int CurrentHp;
            internal int CurrentMana;
            internal int CurrentStamina;
            internal int StrSpent;
            internal int EndSpent;
            internal int DexSpent;
            internal int AgiSpent;
            internal int IntSpent;
            internal int WisSpent;
            internal int ChaSpent;
            internal Stance CombatStance;
            internal bool StopAllRegen;
            internal Spell Aura;
            internal Vector3 Position;
            internal Quaternion Rotation;
            internal Character Target;
            internal bool PlayerTyping;
            internal bool AutoEngage;
            internal bool InCombat;
            internal bool Autoattacking;
            internal float TimeScale;
            internal UnityEngine.Random.State RandomState;
            internal readonly List<Spell> KnownSpells = new List<Spell>();
            internal readonly List<Skill> KnownSkills = new List<Skill>();
            internal readonly List<AscensionSkillEntry> Ascensions =
                new List<AscensionSkillEntry>();
            internal int AscensionPoints;
            internal readonly List<float> HotkeyCooldowns = new List<float>();
            internal readonly List<StatusState> Statuses = new List<StatusState>();
            internal InventoryState Inventory;
        }

        private sealed class RuntimeContext
        {
            internal GameObject DummyObject;
            internal Character Dummy;
            internal Stats DummyStats;
            internal NPC Pet;
            internal readonly List<Item> EquipmentClones = new List<Item>();
            internal Item PetWeaponClone;
            internal string WeaponDescription;
            internal string LoadoutDescription;
            internal string StatDescription;
            internal string ActiveEffectDescription;
            internal string PetWeaponDescription = "none";
            internal int PetSummonCost;
            internal int PetSummonMaximumMana;
            internal bool PetSummonAffordable = true;
        }

        internal static bool IsRunning { get; private set; }
        internal static bool AllowGuardedSave { get; private set; }
        internal static string LastReportPath { get; private set; }
        private static RuntimeContext _activeRuntime;
        private static ParseResult _activeResult;
        private static int _statusTickDepth;
        private static int _wandBoltDepth;
        private static int _skillActionDepth;
        private static string _activeSkillName;
        private static float _requestedTimeScale = -1f;
        private static readonly Stack<Spell> ResolvingSpells =
            new Stack<Spell>();

        internal static Character ActiveDummy => _activeRuntime?.Dummy;
        internal static Stats ActiveDummyStats => _activeRuntime?.DummyStats;

        internal static void EnforceRequestedTimeScale()
        {
            if (!IsRunning || _requestedTimeScale <= 0f) return;
            if (!Mathf.Approximately(Time.timeScale, _requestedTimeScale))
                Time.timeScale = _requestedTimeScale;
            if (Application.targetFrameRate != BenchmarkTargetFrameRate)
                Application.targetFrameRate = BenchmarkTargetFrameRate;
            if (QualitySettings.vSyncCount != 0)
                QualitySettings.vSyncCount = 0;
        }

        internal static bool ShouldSuppressDamage(Character target,
            Character attacker)
        {
            if (!IsRunning) return false;
            if (target != ActiveDummy) return true;
            return !IsAuthorizedAttacker(attacker);
        }

        internal static bool ShouldSuppressEnvironmentalDamage(Character target)
        {
            // None of the benchmark rotations uses environmental damage. Block
            // it everywhere so world hazards cannot contaminate the dummy.
            return IsRunning;
        }

        internal static bool ShouldSuppressStatus(Stats target,
            Character caster, Spell spell)
        {
            if (!IsRunning) return false;
            // Native percent-current-health bleeds cap at 10,000 damage per
            // tick. Their result depends on the benchmark dummy's artificial
            // 500M HP pool rather than class/gear power, so reject the entire
            // proc status at application time.
            if (target == ActiveDummyStats &&
                spell != null && spell.BleedDamagePercent > 0)
                return true;
            if (target == ActiveDummyStats)
                return !IsAuthorizedAttacker(caster);
            return target != ActiveDummyStats &&
                target != GameData.PlayerStats &&
                target != _activeRuntime?.Pet?.GetComponent<Stats>();
        }

        private static bool IsAuthorizedAttacker(Character attacker)
        {
            if (attacker != null)
                return attacker == GameData.PlayerControl?.Myself ||
                    IsPetAttacker(attacker);

            // Native periodic ticks and several spell/skill resolutions do
            // not forward their caster. Only accept that null source while a
            // controlled benchmark action is synchronously resolving.
            return _statusTickDepth > 0 || _skillActionDepth > 0 ||
                ResolvingSpells.Count > 0;
        }

        internal static void BeginStatusTick(Stats stats)
        {
            if (IsRunning && stats == ActiveDummyStats) _statusTickDepth++;
        }

        internal static void EndStatusTick(Stats stats)
        {
            if (IsRunning && stats == ActiveDummyStats)
                _statusTickDepth = Math.Max(0, _statusTickDepth - 1);
        }

        internal static void BeginWandBolt()
        {
            if (IsRunning) _wandBoltDepth++;
        }

        internal static void EndWandBolt()
        {
            if (IsRunning) _wandBoltDepth = Math.Max(0, _wandBoltDepth - 1);
        }

        internal static void BeginSpellResolve(Spell spell)
        {
            if (IsRunning) ResolvingSpells.Push(spell);
        }

        internal static void EndSpellResolve()
        {
            if (ResolvingSpells.Count > 0) ResolvingSpells.Pop();
        }

        private static void BeginSkillAction(Skill skill)
        {
            _skillActionDepth++;
            _activeSkillName = skill?.SkillName;
        }

        private static void EndSkillAction()
        {
            _skillActionDepth = Math.Max(0, _skillActionDepth - 1);
            if (_skillActionDepth == 0) _activeSkillName = null;
        }

        private static void ActivateRuntime(RuntimeContext runtime)
        {
            _activeRuntime = runtime;
        }

        private static void BeginAttribution(RuntimeContext runtime,
            ParseResult result)
        {
            _activeRuntime = runtime;
            _activeResult = result;
            _statusTickDepth = 0;
            _wandBoltDepth = 0;
            _skillActionDepth = 0;
            _activeSkillName = null;
            ResolvingSpells.Clear();
        }

        private static void EndAttribution(ParseResult result)
        {
            if (result != null && ReferenceEquals(result, _activeResult))
            {
                long accounted = result.WeaponDamage + result.SkillDamage +
                    result.DirectSpellDamage + result.AreaSpellDamage +
                    result.DotDamage + result.PetDamage;
                result.OtherDamage = result.Damage - accounted;
            }
            _activeResult = null;
            _statusTickDepth = 0;
            _wandBoltDepth = 0;
            _skillActionDepth = 0;
            _activeSkillName = null;
            ResolvingSpells.Clear();
        }

        private static bool IsPetAttacker(Character attacker)
        {
            if (attacker == null || _activeRuntime?.Pet == null) return false;
            Character pet = _activeRuntime.Pet.GetComponent<Character>();
            return attacker == pet;
        }

        private static void RecordSpellDamage(int damage)
        {
            Spell resolving = ResolvingSpells.Count > 0
                ? ResolvingSpells.Peek() : null;
            AddDamageSource(_activeResult, "spell",
                resolving?.SpellName ?? "unresolved", damage);
            if (resolving != null &&
                (resolving.Type == Spell.SpellType.AE ||
                 resolving.Type == Spell.SpellType.PBAE))
                _activeResult.AreaSpellDamage += damage;
            else
                _activeResult.DirectSpellDamage += damage;
        }

        internal static void RecordPhysicalDamage(Character target,
            Character attacker, int damage)
        {
            if (_activeResult == null || target != ActiveDummy || damage <= 0)
                return;
            if (IsPetAttacker(attacker))
            {
                _activeResult.PetDamage += damage;
                AddDamageSource(_activeResult, "pet",
                    attacker?.name ?? "bone thrall", damage);
            }
            else if (_statusTickDepth > 0)
            {
                _activeResult.DotDamage += damage;
                AddDamageSource(_activeResult, "dot", "periodic", damage);
            }
            else if (ResolvingSpells.Count > 0) RecordSpellDamage(damage);
            else if (_skillActionDepth > 0 ||
                Monk.MonkRuntime.TechniqueDamageActive)
            {
                _activeResult.SkillDamage += damage;
                AddDamageSource(_activeResult, "skill",
                    _activeSkillName ?? "Monk technique", damage);
            }
            else
            {
                _activeResult.WeaponDamage += damage;
                AddDamageSource(_activeResult, "weapon", "autoattack", damage);
            }
        }

        internal static void RecordMagicDamage(Character target,
            Character attacker, int damage)
        {
            if (_activeResult == null || target != ActiveDummy || damage <= 0)
                return;
            if (IsPetAttacker(attacker))
            {
                _activeResult.PetDamage += damage;
                AddDamageSource(_activeResult, "pet",
                    attacker?.name ?? "bone thrall", damage);
            }
            else if (_statusTickDepth > 0)
            {
                _activeResult.DotDamage += damage;
                AddDamageSource(_activeResult, "dot", "periodic", damage);
            }
            else if (_wandBoltDepth > 0)
            {
                _activeResult.WeaponDamage += damage;
                AddDamageSource(_activeResult, "weapon", "wand bolt", damage);
            }
            else RecordSpellDamage(damage);
        }

        internal static void RecordBleedDamage(Character target,
            Character attacker, int damage)
        {
            if (_activeResult == null || target != ActiveDummy || damage <= 0)
                return;
            if (IsPetAttacker(attacker))
            {
                _activeResult.PetDamage += damage;
                AddDamageSource(_activeResult, "pet",
                    attacker?.name ?? "bone thrall", damage);
            }
            else
            {
                _activeResult.DotDamage += damage;
                AddDamageSource(_activeResult, "dot", "bleed", damage);
            }
        }

        internal static void RecordEnvironmentalDamage(Character target,
            int damage)
        {
            if (_activeResult != null && target == ActiveDummy && damage > 0)
            {
                _activeResult.OtherDamage += damage;
                AddDamageSource(_activeResult, "other", "environment", damage);
            }
        }

        internal static IEnumerator Run(string options)
        {
            if (IsRunning)
            {
                ChatHelper.Send(
                    "<color=#FFD54F>[Test Mode]</color> The live DPS benchmark is already running.");
                yield break;
            }

            if (!TryParseBenchmarkOptions(options, out BenchmarkOptions run))
            {
                ChatHelper.Send(
                    "<color=#EF5350>[Test Mode]</color> Usage: /cstest necro dps " +
                    "[quick|full|progression|progression-sustained|" +
                    "progression-quick|audit-druid|level " +
                    "5|10|15|20|25|30|35 [quick|balance|full]|" +
                    "monk-quick|monk-timescale|monk-audit|monk-retune]");
                yield break;
            }

            IsRunning = true;
            IEnumerator core = RunCore(run);
            Exception failure = null;
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
                    failure = ex;
                    moved = false;
                }
                if (!moved) break;
                yield return current;
            }
            (core as IDisposable)?.Dispose();
            IsRunning = false;

            if (failure != null)
            {
                SkillsPlugin.Log.LogError(
                    "Live DPS benchmark aborted safely: " + failure);
                ChatHelper.Send(
                    "<color=#EF5350>[Test Mode]</color> DPS benchmark aborted after restoring John: " +
                    failure.Message);
            }
        }

        private static bool TryParseBenchmarkOptions(string value,
            out BenchmarkOptions options)
        {
            options = null;
            string normalized = (value ?? "").Trim().ToLowerInvariant();
            if (normalized.Length == 0 || normalized == "full")
            {
                options = new BenchmarkOptions
                {
                    Quick = false,
                    Levels = new[] { MaximumLevel },
                    Slug = "full",
                    DisplayName = "Full level-35"
                };
                return true;
            }
            if (normalized == "quick")
            {
                options = new BenchmarkOptions
                {
                    Quick = true,
                    Levels = new[] { MaximumLevel },
                    Slug = "quick",
                    DisplayName = "Quick level-35 validation"
                };
                return true;
            }
            if (normalized == "progression" ||
                normalized == "progression-full")
            {
                options = new BenchmarkOptions
                {
                    Quick = false,
                    Levels = ProgressionLevels.ToArray(),
                    Slug = "progression-balance",
                    DisplayName = "Real-time level 5-35 progression balance",
                    Durations = new[] { 60 },
                    Repetitions = 3,
                    TimeScales = new[] { FullTimeScale }
                };
                return true;
            }
            if (normalized == "progression-sustained")
            {
                options = new BenchmarkOptions
                {
                    Quick = false,
                    Levels = ProgressionLevels.ToArray(),
                    Slug = "progression-sustained",
                    DisplayName = "Sustained level 5-35 progression"
                };
                return true;
            }
            if (normalized == "progression-quick" ||
                normalized == "quick-progression")
            {
                options = new BenchmarkOptions
                {
                    Quick = true,
                    Levels = ProgressionLevels.ToArray(),
                    Slug = "progression-quick",
                    DisplayName = "Quick level 5-35 progression validation"
                };
                return true;
            }
            if (normalized == "audit-druid" ||
                normalized == "druid-audit")
            {
                options = new BenchmarkOptions
                {
                    Levels = new[] { MaximumLevel },
                    Slug = "audit-druid-timescale",
                    DisplayName = $"Druid 1x/{AcceleratedAuditTimeScale:F0}x time-scale audit",
                    ProfileNames = new[] { "Druid" },
                    Durations = new[] { 60 },
                    Repetitions = 1,
                    TimeScales = new[] { 1f, AcceleratedAuditTimeScale },
                    DiagnosticOnly = true
                };
                return true;
            }
            if (normalized == "monk-quick")
            {
                options = new BenchmarkOptions
                {
                    Quick = true,
                    Levels = ProgressionLevels.ToArray(),
                    Slug = "monk-gear-progression-quick",
                    DisplayName =
                        "Quick Monk level and gear-power validation",
                    Durations = new[] { 15 },
                    Repetitions = 1,
                    TimeScales = new[] { AcceleratedAuditTimeScale },
                    GearPowers = new[]
                    {
                        GearPower.Budget,
                        GearPower.Progression,
                        GearPower.Optimized
                    },
                    IncludeMonk = true,
                    MonkPeerGuard = true,
                    ReportCategory = "MonkDps",
                    ReportFileName = "monk-dps-benchmark.txt"
                };
                return true;
            }
            if (normalized == "monk-timescale")
            {
                options = new BenchmarkOptions
                {
                    Levels = new[] { MaximumLevel },
                    Slug = "monk-timescale-calibration",
                    DisplayName =
                        $"All-class 1x/{AcceleratedAuditTimeScale:F0}x time-scale calibration",
                    Durations = new[] { 60 },
                    Repetitions = 2,
                    TimeScales = new[]
                        { FullTimeScale, AcceleratedAuditTimeScale },
                    GearPowers = new[] { GearPower.Optimized },
                    DiagnosticOnly = true,
                    IncludeMonk = true,
                    TimeScaleGuard = true,
                    ReportCategory = "MonkDps",
                    ReportFileName = "monk-dps-benchmark.txt"
                };
                return true;
            }
            if (normalized == "monk-audit" ||
                normalized == "monk-full")
            {
                options = new BenchmarkOptions
                {
                    Levels = ProgressionLevels.ToArray(),
                    Slug = "monk-gear-progression-full",
                    DisplayName =
                        "Full Monk level and gear-power balance audit",
                    Durations = new[] { 60 },
                    Repetitions = 3,
                    TimeScales = new[] { AcceleratedAuditTimeScale },
                    GearPowers = new[]
                    {
                        GearPower.Budget,
                        GearPower.Progression,
                        GearPower.Optimized
                    },
                    IncludeMonk = true,
                    MonkPeerGuard = true,
                    ReportCategory = "MonkDps",
                    ReportFileName = "monk-dps-benchmark.txt"
                };
                return true;
            }
            if (normalized == "monk-retune" ||
                normalized == "monk-verify")
            {
                options = new BenchmarkOptions
                {
                    Levels = new[] { 20, 30, 35 },
                    Slug = "monk-retune-verification",
                    DisplayName = "Focused Monk retune verification",
                    ProfileNames = new[] { "Monk" },
                    Durations = new[] { 60 },
                    Repetitions = 3,
                    TimeScales = new[] { AcceleratedAuditTimeScale },
                    GearPowers = new[]
                    {
                        GearPower.Budget,
                        GearPower.Progression,
                        GearPower.Optimized
                    },
                    DiagnosticOnly = true,
                    IncludeMaxAscension = true,
                    IncludeMonk = true,
                    ReportCategory = "MonkDps",
                    ReportFileName = "monk-dps-benchmark.txt"
                };
                return true;
            }

            string[] parts = normalized.Split(new[] { ' ' },
                StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length >= 2 && parts.Length <= 3 &&
                parts[0] == "level" && int.TryParse(parts[1], out int level) &&
                ProgressionLevels.Contains(level) &&
                (parts.Length == 2 || parts[2] == "quick" ||
                 parts[2] == "balance" || parts[2] == "full"))
            {
                bool quick = parts.Length == 3 && parts[2] == "quick";
                bool balance = parts.Length == 3 && parts[2] == "balance";
                options = new BenchmarkOptions
                {
                    Quick = quick,
                    Levels = new[] { level },
                    Slug = $"level-{level}-" + (quick ? "quick" :
                        balance ? "balance" : "full"),
                    DisplayName = $"{(quick ? "Quick" : balance ?
                        "Real-time balance" : "Full")} level-{level}",
                    Durations = balance ? new[] { 60 } : null,
                    Repetitions = balance ? 2 : 0,
                    TimeScales = balance ? new[] { FullTimeScale } : null
                };
                return true;
            }
            return false;
        }

        private static IEnumerator RunCore(BenchmarkOptions options)
        {
            PlayerState original = null;
            RuntimeContext runtime = null;
            bool originalRunInBackground = Application.runInBackground;
            int originalTargetFrameRate = Application.targetFrameRate;
            int originalVSyncCount = QualitySettings.vSyncCount;
            int originalQualityLevel = QualitySettings.GetQualityLevel();
            int originalScreenWidth = Screen.width;
            int originalScreenHeight = Screen.height;
            bool originalFullScreen = Screen.fullScreen;
            var originalCameraMasks = new Dictionary<Camera, int>();
            var results = new List<ParseResult>();
            string runId = DateTime.UtcNow.ToString("yyyyMMdd-HHmmss") +
                "-John_EverQuest-" + options.Slug;
            string category = options.ReportCategory ??
                (options.IsProgression ? "ProgressionDps" :
                    $"Level{options.Levels[0]}Dps");
            string directory = Path.Combine(RuntimePaths.DataDirectory,
                "TestReports", category, runId);
            Directory.CreateDirectory(directory);
            string reportName = options.ReportFileName ??
                (options.IsProgression
                    ? "level-progression-dps-benchmark.txt"
                    : $"level{options.Levels[0]}-dps-benchmark.txt");
            LastReportPath = Path.Combine(directory, reportName);

            try
            {
                ValidateRuntimePreconditions();
                Application.runInBackground = true;
                Application.targetFrameRate = BenchmarkTargetFrameRate;
                QualitySettings.vSyncCount = 0;
                QualitySettings.SetQualityLevel(0, true);
                Screen.SetResolution(1280, 720, false,
                    BenchmarkTargetFrameRate);
                foreach (Camera camera in Camera.allCameras)
                {
                    if (camera == null || !camera.enabled) continue;
                    originalCameraMasks[camera] = camera.cullingMask;
                    camera.cullingMask = 0;
                }
                if (options.IncludeMonk)
                {
                    Monk.MonkCatalog.TryInitialize();
                    if (!Monk.MonkCatalog.IsReady ||
                        !Monk.MonkGear.IsReady ||
                        !Monk.MonkAuras.IsReady)
                        throw new InvalidOperationException(
                            "the Monk class, gear, or aura catalog is not ready");
                }
                original = CapturePlayerState();
                _standardBaseHp = Math.Max(1, original.BaseHp);
                _standardBaseMana = Math.Max(0, original.BaseMana);
                SaveNativeState();

                List<ClassProfile> profiles =
                    BuildProfiles(options.IncludeMonk);
                if (options.ProfileNames != null)
                    profiles = profiles.Where(value => options.ProfileNames
                        .Contains(value.Name, StringComparer.OrdinalIgnoreCase))
                        .ToList();
                if (profiles.Count == 0)
                    throw new InvalidOperationException(
                        "the selected DPS audit has no matching class profile");
                List<ParseSpec> specs = BuildSpecs(profiles, options);
                GameData.PlayerTyping = true;

                WriteReport(results, options, false, null);
                ChatHelper.Send(
                    $"<color=#FFD54F>[Test Mode]</color> {options.DisplayName} " +
                    $"live DPS benchmark started: {specs.Count} parses at " +
                    $"{string.Join("/", specs.Select(value => value.TimeScale)
                        .Distinct().Select(value => value.ToString("F0") + "x"))} " +
                    "simulation speed.");

                for (int index = 0; index < specs.Count; index++)
                {
                    ParseSpec spec = specs[index];
                    int attempt = 0;
                    while (true)
                    {
                        attempt++;
                        _requestedTimeScale = spec.TimeScale;
                        Time.timeScale = spec.TimeScale;
                        runtime = new RuntimeContext();
                        IEnumerator parse = RunParse(spec, runtime, original,
                            results, index + 1, specs.Count);
                        Exception parseFailure = null;
                        while (true)
                        {
                            bool moved;
                            object current = null;
                            try
                            {
                                moved = parse.MoveNext();
                                if (moved) current = parse.Current;
                            }
                            catch (Exception ex)
                            {
                                parseFailure = ex;
                                moved = false;
                            }
                            if (!moved) break;
                            yield return current;
                        }
                        (parse as IDisposable)?.Dispose();
                        if (parseFailure == null) break;
                        CleanupRuntime(runtime);
                        runtime = null;
                        ClearStatuses(GameData.PlayerStats);
                        if (!(parseFailure is ParseCadenceException) ||
                            attempt >= MaximumCadenceAttempts)
                            throw parseFailure;

                        SkillsPlugin.Log.LogWarning(
                            $"[DPS retry] L{spec.Level} " +
                            $"{spec.Profile.Name} {spec.GearPower} cadence " +
                            $"attempt {attempt}/{MaximumCadenceAttempts} " +
                            $"discarded: {parseFailure.Message}");
                        ChatHelper.Send(
                            $"<color=#FFD54F>[DPS retry]</color> " +
                            $"{spec.Profile.Name} cadence sample discarded; " +
                            $"retrying after host load settles " +
                            $"({attempt}/{MaximumCadenceAttempts}).");
                        _requestedTimeScale = FullTimeScale;
                        Time.timeScale = FullTimeScale;
                        float retryAt = Time.realtimeSinceStartup + 5f;
                        while (Time.realtimeSinceStartup < retryAt)
                            yield return null;
                    }
                    CleanupRuntime(runtime);
                    runtime = null;
                    ClearStatuses(GameData.PlayerStats);
                    yield return null;
                    WriteReport(results, options, false, null);
                }

                bool guardPassed;
                string guard;
                if (options.TimeScaleGuard)
                    guard = BuildTimeScaleGuard(results, null,
                        out guardPassed);
                else if (options.DiagnosticOnly)
                {
                    guardPassed = true;
                    guard = "DIAGNOSTIC ONLY: this run compares simulation " +
                        "speeds and does not make a Necromancer balance claim.";
                }
                else if (options.MonkPeerGuard)
                    guard = BuildPeerGuard(results, "Monk", options.Quick,
                        out guardPassed);
                else
                    guard = BuildPeerGuard(results, "Necromancer",
                        options.Quick, out guardPassed);
                WriteReport(results, options, true, guard);
                string guardName = options.TimeScaleGuard
                    ? "time-scale"
                    : options.MonkPeerGuard ? "Monk peer"
                    : "Necromancer peer";
                bool guardNotApplicable =
                    options.DiagnosticOnly && !options.TimeScaleGuard;
                SkillsPlugin.Log.LogInfo(
                    $"{options.DisplayName} DPS benchmark complete: " +
                    $"parses={results.Count}; " +
                    $"guard={(guardNotApplicable ? "N/A" :
                        (guardPassed ? "PASS" : "FAIL"))}; path={LastReportPath}");
                ChatHelper.Send(
                    $"<color={(guardPassed ? "#4CAF50" : "#EF5350")}>[Test Mode]</color> " +
                    $"{options.DisplayName} DPS benchmark complete: " +
                    $"{results.Count} live parses" +
                    (guardNotApplicable ? "." :
                        $"; {guardName} guard=" +
                        $"{(guardPassed ? "PASS" : "FAIL")}."));
                ChatHelper.Send("<color=#AAAAAA>" + guard + "</color>");
                ChatHelper.Send("<color=#AAAAAA>Report: " +
                    LastReportPath + "</color>");
            }
            finally
            {
                _requestedTimeScale = -1f;
                Application.runInBackground = originalRunInBackground;
                Application.targetFrameRate = originalTargetFrameRate;
                QualitySettings.vSyncCount = originalVSyncCount;
                QualitySettings.SetQualityLevel(originalQualityLevel, true);
                Screen.SetResolution(originalScreenWidth,
                    originalScreenHeight, originalFullScreen);
                foreach (KeyValuePair<Camera, int> entry in
                    originalCameraMasks)
                    if (entry.Key != null)
                        entry.Key.cullingMask = entry.Value;
                CleanupRuntime(runtime);
                if (original != null)
                {
                    RestorePlayerState(original);
                    IsRunning = false;
                    SaveNativeState();
                    IsRunning = true;
                }
                AllowGuardedSave = false;
                _activeLevel = MaximumLevel;
                _activeGearPower = GearPower.Optimized;
            }
        }

        private static List<ParseSpec> BuildSpecs(
            List<ClassProfile> profiles, BenchmarkOptions options)
        {
            var output = new List<ParseSpec>();
            int[] durations = options.Durations ?? (options.Quick
                ? new[] { 12, 30 } : new[] { 60, 300 });
            int repetitions = options.Repetitions > 0
                ? options.Repetitions : (options.Quick ? 1 : 2);
            float[] timeScales = options.TimeScales ?? new[]
                { options.Quick ? QuickTimeScale : FullTimeScale };
            GearPower[] gearPowers = options.GearPowers ??
                new[] { GearPower.Optimized };
            foreach (int level in options.Levels)
                foreach (int duration in durations)
                    for (int repetition = 1; repetition <= repetitions;
                        repetition++)
                        foreach (GearPower gearPower in gearPowers)
                            foreach (ClassProfile profile in profiles)
                                foreach (float timeScale in timeScales)
                        {
                            // Ascension XP begins only at Erenshor's level cap.
                            // Quick runs are structural validation; full level-35
                            // runs pair baseline and fully developed class trees.
                            bool[] scenarios = !options.Quick &&
                                (!options.DiagnosticOnly ||
                                    options.IncludeMaxAscension) &&
                                level == MaximumLevel
                                ? new[] { false, true }
                                : new[] { false };
                            foreach (bool maxAscension in scenarios)
                                output.Add(new ParseSpec
                                {
                                    Profile = profile,
                                    Level = level,
                                    MaxAscension = maxAscension,
                                    DurationSeconds = duration,
                                    TimeScale = timeScale,
                                    Repetition = repetition,
                                    GearPower = gearPower,
                                    // Pair all classes and the level-35
                                    // Ascension scenario within a gear tier on
                                    // the same RNG seed.
                                    Seed = level * 100000 + duration * 10 +
                                        repetition +
                                        ((int)gearPower + 1) * 10000
                                });
                        }
            return output;
        }

        private static IEnumerator RunParse(ParseSpec spec,
            RuntimeContext runtime, PlayerState original,
            List<ParseResult> results, int parseNumber, int parseCount)
        {
            ValidateWorldStillLoaded();
            _activeLevel = spec.Level;
            _activeGearPower = spec.GearPower;
            ConfigurePlayer(spec.Profile, spec.MaxAscension, original,
                runtime);
            IEnumerator dummySetup = SpawnDummy(runtime);
            while (dummySetup.MoveNext()) yield return dummySetup.Current;
            ActivateRuntime(runtime);

            List<Spell> spells = GetSpells(spec.Profile).ToList();
            IEnumerator petSetup = SummonPet(spec.Profile, spells, runtime);
            while (petSetup.MoveNext()) yield return petSetup.Current;
            // Equipment can be swapped while Unity is still settling a prior
            // parse. Rebuild every player effect at the actual combat boundary
            // so the first repetition receives the same worn effects, aura,
            // and self buffs as later repetitions.
            ClearStatuses(GameData.PlayerStats);
            GameData.PlayerStats.MyAura = null;
            GameData.PlayerStats.CheckAuras();
            ApplyPrecombatBuffs(spec.Profile, spells, runtime);
            GameData.PlayerStats.CalcStats();
            GameData.PlayerStats.CurrentHP = GameData.PlayerStats.CurrentMaxHP;
            GameData.PlayerStats.CurrentMana =
                GameData.PlayerStats.GetCurrentMaxMana();
            runtime.StatDescription = DescribeCurrentPlayerStats();
            runtime.ActiveEffectDescription = DescribeActiveEffects();
            SkillsPlugin.Log.LogInfo($"[DPS] L{_activeLevel} " +
                $"{spec.Profile.Name} combat-boundary stats: " +
                runtime.StatDescription + "; effects=" +
                runtime.ActiveEffectDescription);
            runtime.DummyStats.CurrentHP = DummyHealth;
            ClearStatuses(runtime.DummyStats);
            GameData.PlayerStats.ZeroMHAtkDelay();
            GameData.PlayerStats.ResetOHAtkDelay(5f);
            GameData.PlayerControl.CurrentTarget = runtime.Dummy;
            EnsureCombatRange(runtime.Dummy);
            // Setup paths consume Unity's global RNG differently between
            // classes and Ascension profiles. Seed at the actual combat
            // boundary so paired scenarios begin from the same sequence.
            UnityEngine.Random.InitState(spec.Seed);

            int startingMana = GameData.PlayerStats.CurrentMana;
            int startingPlayerHealth = GameData.PlayerStats.CurrentHP;
            int startingHealth = runtime.DummyStats.CurrentHP;
            var result = new ParseResult
            {
                ClassName = spec.Profile.Name,
                Level = spec.Level,
                Scenario = spec.MaxAscension ? "max-class-ascension" : "baseline",
                DurationSeconds = spec.DurationSeconds,
                TimeScale = spec.TimeScale,
                Seed = spec.Seed,
                Repetition = spec.Repetition,
                GearPower = spec.GearPower.ToString().ToLowerInvariant(),
                GearLevelCeiling = ActiveGearLevelCeiling,
                StartingMana = startingMana,
                StartingHealth = startingPlayerHealth,
                MinimumHealth = startingPlayerHealth,
                Weapon = runtime.WeaponDescription,
                Loadout = runtime.LoadoutDescription,
                Stats = runtime.StatDescription,
                ActiveEffects = runtime.ActiveEffectDescription,
                Pet = runtime.Pet == null ? "none" :
                    (runtime.Pet.NPCName ?? runtime.Pet.name),
                PetWeapon = runtime.PetWeaponDescription,
                PetSummonCost = runtime.PetSummonCost,
                PetSummonMaximumMana = runtime.PetSummonMaximumMana,
                PetSummonAffordable = runtime.PetSummonAffordable
            };
            ValidateComparableConfiguration(result, results);
            var spellReady = new Dictionary<Spell, float>();
            var skillReady = new Dictionary<Skill, float>();
            // Erenshor can rewrite Time.timeScale after equipment, aura, or
            // scene state settles. Reassert the requested benchmark scale at
            // the combat boundary and on every frame so accelerated parses do
            // not silently fall back to wall-clock speed.
            Time.timeScale = spec.TimeScale;
            float startTime = Time.time;
            float startWallTime = Time.realtimeSinceStartup;
            float nextAction = startTime;
            CastSpell caster = GameData.PlayerControl.Myself.MySpells;
            UseSkill skills = GameData.PlayerControl.Myself.MySkills;
            List<Skill> activeSkills = skills.KnownSkills
                .Where(value => IsOffensiveSkill(spec.Profile, value))
                .Where(value => SkillRequirementsMet(value,
                    spec.Profile.Weapon)).ToList();
            List<Skill> damageBuffSkills = skills.KnownSkills
                .Where(value => IsDamageBuffSkill(spec.Profile, value))
                .ToList();
            BeginAttribution(runtime, result);
            GameData.PlayerCombat.ForceAttackOn();

            ChatHelper.Send(
                $"<color=#AAAAAA>[DPS {parseNumber}/{parseCount}]</color> " +
                $"L{spec.Level} {spec.Profile.Name}, {result.Scenario}, " +
                $"{result.GearPower} gear (ceiling L" +
                $"{result.GearLevelCeiling}), " +
                $"{spec.DurationSeconds}s at {spec.TimeScale:F0}x, " +
                $"seed {spec.Seed}, {runtime.WeaponDescription}, pet={result.Pet}.");
            SkillsPlugin.Log.LogInfo(
                $"[DPS] {spec.Profile.Name} stance=" +
                $"{GameData.PlayerStats.CombatStance?.DisplayName ?? "none"} " +
                $"selfAttack=" +
                $"{GameData.PlayerStats.CombatStance?.SelfDamagePerAttack ?? 0f} " +
                $"selfCast=" +
                $"{GameData.PlayerStats.CombatStance?.SelfDamagePerCast ?? 0f}");

            while (Time.time - startTime < spec.DurationSeconds)
            {
                result.SimulationFrames++;
                if (!Mathf.Approximately(Time.timeScale, spec.TimeScale))
                    Time.timeScale = spec.TimeScale;
                ValidateWorldStillLoaded();
                result.MinimumHealth = Math.Min(result.MinimumHealth,
                    GameData.PlayerStats.CurrentHP);
                if (!GameData.PlayerControl.Myself.Alive ||
                    GameData.PlayerStats.CurrentHP <= 0)
                {
                    Stance stance = GameData.PlayerStats.CombatStance;
                    throw new InvalidOperationException(
                        $"{spec.Profile.Name} entered a death state during " +
                        $"the {spec.DurationSeconds}s parse: hp=" +
                        $"{GameData.PlayerStats.CurrentHP}/" +
                        $"{GameData.PlayerStats.CurrentMaxHP}, mana=" +
                        $"{GameData.PlayerStats.CurrentMana}, stance=" +
                        $"{stance?.DisplayName ?? "none"} " +
                        $"(selfAttack={stance?.SelfDamagePerAttack ?? 0f}, " +
                        $"selfCast={stance?.SelfDamagePerCast ?? 0f}), " +
                        $"spells={Counts(result.Spells)}, " +
                        $"skills={Counts(result.Skills)}");
                }
                EnsureCombatRange(runtime.Dummy);
                if (runtime.Pet != null &&
                    runtime.Pet.CurrentAggroTarget == null)
                    runtime.Pet.AggroOn(runtime.Dummy);

                float now = Time.time;
                if (caster.isCasting())
                {
                    // Inputs cannot queue while a native spell is casting.
                    // Keep the scheduler at the current combat time so it
                    // does not replay the cast's elapsed action slots later.
                    nextAction = Math.Max(nextAction, now);
                }
                else if (now >= nextAction)
                {
                    int replayed = 0;
                    while (!caster.isCasting() && now >= nextAction &&
                        replayed < MaximumActionReplaysPerFrame)
                    {
                        result.ActionOpportunities++;
                        bool acted = TryUseSkill(damageBuffSkills, skillReady,
                            now, GameData.PlayerControl.Myself, skills, result);
                        if (!acted)
                            acted = TryUseRecovery(spells, spellReady,
                                now, result);
                        if (!acted)
                            acted = TryUseSkill(activeSkills, skillReady,
                                now, runtime.Dummy, skills, result);
                        if (!acted)
                            acted = TryCastDamageSpell(spells, spellReady,
                                now, runtime.DummyStats, spec.Sustained,
                                caster, result);
                        if (acted) result.SuccessfulActions++;
                        nextAction += acted ? 0.08f : 0.20f;
                        replayed++;
                        if (!acted) break;
                    }
                    if (replayed == MaximumActionReplaysPerFrame &&
                        now >= nextAction)
                        nextAction = now + 0.08f;
                }
                yield return null;
            }

            GameData.PlayerCombat.ForceAttackOff();
            result.WallDurationSeconds = Math.Max(0.001,
                Time.realtimeSinceStartup - startWallTime);
            ValidateParseCadence(spec, result);
            int endingHealth = Mathf.Clamp(runtime.DummyStats.CurrentHP,
                0, DummyHealth);
            result.Damage = Math.Max(0L,
                (long)startingHealth - endingHealth);
            result.Dps = result.Damage / (double)spec.DurationSeconds;
            result.EndingMana = GameData.PlayerStats.CurrentMana;
            result.EndingHealth = GameData.PlayerStats.CurrentHP;
            result.MinimumHealth = Math.Min(result.MinimumHealth,
                result.EndingHealth);
            result.PlayerAlive = GameData.PlayerControl.Myself.Alive;
            EndAttribution(result);
            long attributionTolerance = Math.Max(10L,
                (long)Math.Ceiling(result.Damage * 0.005));
            if (Math.Abs(result.OtherDamage) > attributionTolerance)
                throw new InvalidOperationException(
                    $"{result.ClassName} damage attribution differed from " +
                    $"the native HP delta by {result.OtherDamage} " +
                    $"(tolerance {attributionTolerance})");
            results.Add(result);
            caster.InterruptCast();

            // Let already-launched projectiles and VFX settle only after the
            // measurement is frozen, so they cannot leak into the next parse.
            float settleUntil = Time.time + 1.5f;
            while (Time.time < settleUntil) yield return null;
            SkillsPlugin.Log.LogInfo(
                $"[DPS] L{result.Level} {result.ClassName} {result.Scenario} " +
                $"{result.GearPower} gear ceiling=L{result.GearLevelCeiling} " +
                $"{result.DurationSeconds}s scale={result.TimeScale:F0}x " +
                $"rep={result.Repetition} " +
                $"seed={result.Seed} damage={result.Damage} dps={result.Dps:F2} " +
                $"frames={result.SimulationFrames} wall=" +
                $"{result.WallDurationSeconds:F2}s actions=" +
                $"{result.SuccessfulActions}/{result.ActionOpportunities} " +
                $"sources=weapon:{result.WeaponDamage},skill:{result.SkillDamage}," +
                $"direct:{result.DirectSpellDamage},area:{result.AreaSpellDamage}," +
                $"dot:{result.DotDamage},pet:{result.PetDamage}," +
                $"other:{result.OtherDamage} " +
                $"details={DamageCounts(result.DamageSources)} " +
                $"mana={result.StartingMana}->{result.EndingMana} " +
                $"hp={result.StartingHealth}->{result.MinimumHealth}->" +
                $"{result.EndingHealth} alive={result.PlayerAlive} " +
                $"weapon={result.Weapon} pet={result.Pet}");
        }

        private static void ValidateParseCadence(ParseSpec spec,
            ParseResult result)
        {
            if (spec.TimeScale <= 1.01f) return;
            double effectiveScale =
                spec.DurationSeconds / result.WallDurationSeconds;
            double framesPerSimulatedSecond =
                result.SimulationFrames / (double)spec.DurationSeconds;
            if (effectiveScale >=
                    spec.TimeScale * MinimumCadenceScaleRatio &&
                framesPerSimulatedSecond >=
                    MinimumAcceleratedFramesPerSecond)
                return;
            throw new ParseCadenceException(
                $"{spec.Profile.Name} parse cadence was throttled: " +
                $"requested={spec.TimeScale:F1}x, effective=" +
                $"{effectiveScale:F1}x, frames={result.SimulationFrames} " +
                $"({framesPerSimulatedSecond:F1}/sim-s), wall=" +
                $"{result.WallDurationSeconds:F2}s. No result was accepted.");
        }

        private static bool TryUseRecovery(List<Spell> spells,
            Dictionary<Spell, float> ready, float now, ParseResult result)
        {
            Stats stats = GameData.PlayerStats;
            int maximum = Mathf.Max(1, stats.GetCurrentMaxMana());
            if (stats.CurrentMana > maximum / 4) return false;
            Spell recovery = spells.Where(IsRecoverySpell)
                .Where(value => IsReady(ready, value, now))
                .Where(value => RecoveryIsSafe(value, stats))
                .OrderByDescending(value => value.RequiredLevel)
                .FirstOrDefault();
            if (recovery == null) return false;
            if (!GameData.PlayerControl.Myself.MySpells.StartSpell(recovery,
                    stats)) return false;
            SetSpellReady(ready, recovery, now);
            Increment(result.Spells, recovery.SpellName);
            return true;
        }

        private static bool RecoveryIsSafe(Spell recovery, Stats stats)
        {
            int healthCost = 0;
            if (Necromancer.NecromancerCatalog.TryGetDefinition(recovery,
                    out Necromancer.NecromancerSpellDefinition definition) &&
                definition.Kind == Necromancer.NecromancerSpellKind.HealthToMana)
            {
                if (Necromancer.NecromancerRuntime.HasActiveCovenant(stats))
                    return false;
                healthCost = Mathf.Abs(definition.Hp);
                // A competent Necromancer can deliberately spend health on a
                // non-retaliating dummy, but never cross a 25% survival reserve.
                // This makes the class mechanic usable with real naked HP
                // without restoring the old artificial 50,000-HP safety pool.
                int necromancerReserve =
                    Mathf.CeilToInt(stats.CurrentMaxHP * 0.25f);
                return healthCost > 0 &&
                    stats.CurrentHP - healthCost >= necromancerReserve;
            }
            else if (recovery.InflictOnSelf && recovery.CasterHealing < 0)
                healthCost = recovery.CasterHealing == -4000
                    ? Mathf.RoundToInt(stats.CurrentMaxHP * 0.25f)
                    : -recovery.CasterHealing;
            if (healthCost <= 0) return true;

            // Mirror the native Reaver AI's conservative 4.5x self-damage
            // threshold while also retaining at least ten percent max health.
            int reserve = Math.Max(
                Mathf.CeilToInt(healthCost * 3.5f),
                Mathf.CeilToInt(stats.CurrentMaxHP * 0.10f));
            return stats.CurrentHP > healthCost + reserve;
        }

        private static bool TryUseSkill(List<Skill> candidates,
            Dictionary<Skill, float> ready, float now, Character target,
            UseSkill user, ParseResult result)
        {
            Skill chosen = candidates.Where(value => IsReady(ready, value, now))
                .Where(value => SkillIsSafe(value, GameData.PlayerStats))
                .OrderByDescending(SkillPriority).FirstOrDefault();
            if (chosen == null) return false;
            float cooldownSeconds = Mathf.Max(0.1f, chosen.Cooldown / 60f);
            ready[chosen] = now + cooldownSeconds;
            bool used;
            BeginSkillAction(chosen);
            try { used = user.DoSkill(chosen, target); }
            finally { EndSkillAction(); }
            if (!used) return false;
            Increment(result.Skills, chosen.SkillName);
            return true;
        }

        private static bool SkillIsSafe(Skill skill, Stats stats)
        {
            // Native Reckless Strike (ID shared with its stance asset) costs
            // 15% max HP. Its built-in NPC check can approve a lethal use at
            // only 6.7% HP, so model a competent player retaining 25% reserve.
            if (skill?.Id != "14354340") return true;
            int healthCost = Mathf.RoundToInt(stats.CurrentMaxHP * 0.15f);
            int reserve = Mathf.CeilToInt(stats.CurrentMaxHP * 0.25f);
            return stats.CurrentHP > healthCost + reserve;
        }

        private static bool TryCastDamageSpell(List<Spell> spells,
            Dictionary<Spell, float> ready, float now, Stats target,
            bool sustained, CastSpell caster, ParseResult result)
        {
            int mana = GameData.PlayerStats.CurrentMana;
            List<Spell> dots = spells.Where(IsDamageOverTime)
                .Where(value => value.ManaCost <= mana &&
                    IsReady(ready, value, now) && !HasActiveLine(target, value))
                .OrderByDescending(value => SpellPriority(value, sustained))
                .ToList();
            Spell chosen = dots.FirstOrDefault();
            if (chosen == null)
                chosen = spells.Where(IsDirectDamageSpell)
                    .Where(value => value.ManaCost <= mana &&
                        IsReady(ready, value, now) &&
                        DamageSpellIsSafe(value, GameData.PlayerStats))
                    .OrderByDescending(value => SpellPriority(value, sustained))
                    .FirstOrDefault();
            if (chosen == null) return false;
            Stats castTarget = chosen.Type == Spell.SpellType.PBAE
                ? GameData.PlayerStats : target;
            if (!caster.StartSpell(chosen, castTarget)) return false;
            SetSpellReady(ready, chosen, now);
            Increment(result.Spells, chosen.SpellName);
            return true;
        }

        private static bool DamageSpellIsSafe(Spell spell, Stats stats)
        {
            if (!spell.InflictOnSelf || spell.CasterHealing >= 0) return true;
            int healthCost = spell.CasterHealing == -4000
                ? Mathf.RoundToInt(stats.CurrentMaxHP * 0.25f)
                : -spell.CasterHealing;
            int reserve = Math.Max(
                Mathf.CeilToInt(healthCost * 3.5f),
                Mathf.CeilToInt(stats.CurrentMaxHP * 0.10f));
            return stats.CurrentHP > healthCost + reserve;
        }

        private static void SetSpellReady(Dictionary<Spell, float> ready,
            Spell spell, float now)
        {
            int cooldownRank = GameData.PlayerControl.Myself.MySkills
                .GetAscensionRank("7758218");
            float multiplier = Mathf.Clamp01(1f - cooldownRank * 0.1f);
            ready[spell] = now + Mathf.Max(0f,
                spell.SpellChargeTime / 60f) +
                Mathf.Max(0f, spell.Cooldown) * multiplier;
        }

        private static bool IsReady<T>(Dictionary<T, float> ready,
            T value, float now)
        {
            return !ready.TryGetValue(value, out float until) || now >= until;
        }

        private static bool IsDamageOverTime(Spell spell)
        {
            return spell != null && spell.Type == Spell.SpellType.StatusEffect &&
                spell.TargetDamage > 0 && spell.SpellDurationInTicks > 0 &&
                !spell.CrowdControlSpell && !spell.FearTarget && !spell.RootTarget;
        }

        private static bool IsDirectDamageSpell(Spell spell)
        {
            if (spell == null || spell.TargetDamage <= 0 || spell.SelfOnly ||
                spell.CrowdControlSpell || spell.FearTarget || spell.RootTarget ||
                spell.StunTarget) return false;
            return spell.Type == Spell.SpellType.Damage ||
                spell.Type == Spell.SpellType.AE ||
                spell.Type == Spell.SpellType.PBAE;
        }

        private static bool IsRecoverySpell(Spell spell)
        {
            if (spell == null) return false;
            if (Necromancer.NecromancerCatalog.TryGetDefinition(spell,
                    out Necromancer.NecromancerSpellDefinition definition))
                return definition.Kind ==
                    Necromancer.NecromancerSpellKind.HealthToMana;
            string name = spell.SpellName ?? "";
            return name.IndexOf("Meditative Trance",
                       StringComparison.OrdinalIgnoreCase) >= 0 ||
                   name.IndexOf("Dark Sacrifice",
                       StringComparison.OrdinalIgnoreCase) >= 0 ||
                   name.IndexOf("Dark Ritual",
                       StringComparison.OrdinalIgnoreCase) >= 0 ||
                   name.IndexOf("Dark Pact",
                       StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static double SpellPriority(Spell spell, bool sustained)
        {
            double expected = Math.Max(1, spell.TargetDamage);
            if (IsDamageOverTime(spell))
                expected *= Math.Max(1, spell.SpellDurationInTicks);
            if (spell.StatusEffectToApply != null &&
                spell.StatusEffectToApply.TargetDamage > 0)
                expected += spell.StatusEffectToApply.TargetDamage *
                    Math.Max(1, spell.StatusEffectToApply.SpellDurationInTicks);
            if (sustained)
                return expected / Math.Max(1, spell.ManaCost);
            return expected / Math.Max(0.25,
                spell.SpellChargeTime / 60.0);
        }

        private static bool HasActiveLine(Stats target, Spell spell)
        {
            if (target?.StatusEffects == null) return false;
            foreach (StatusEffect status in target.StatusEffects)
                if (status?.Effect != null && status.Duration > 0f &&
                    (ReferenceEquals(status.Effect, spell) ||
                     status.Effect.Line == spell.Line))
                    return true;
            return false;
        }

        private static bool IsOffensiveSkill(ClassProfile profile, Skill skill)
        {
            if (skill == null || (skill.TypeOfSkill != Skill.SkillType.Attack &&
                skill.TypeOfSkill != Skill.SkillType.Ranged)) return false;
            string name = skill.SkillName ?? "";
            // Kick is the Monk's cast interrupt, not part of its target-dummy
            // damage rotation. Other classes retain their native rotations.
            if (profile.IsMonk && string.Equals(name, "Kick",
                    StringComparison.OrdinalIgnoreCase))
                return false;
            // Percent-current-health and execute effects are encounter tools;
            // an effectively infinite dummy would make them meaningless.
            return name.IndexOf("Razor Tipped", StringComparison.OrdinalIgnoreCase) < 0 &&
                   !string.Equals(name, "Execute",
                       StringComparison.OrdinalIgnoreCase) &&
                   !string.Equals(name, "Imbued Arrow",
                       StringComparison.OrdinalIgnoreCase);
        }

        private static bool IsDamageBuffSkill(ClassProfile profile,
            Skill skill)
        {
            if (!profile.IsMonk ||
                !Monk.MonkCatalog.TryGetDefinition(skill,
                    out Monk.MonkTechniqueDefinition definition))
                return false;
            return definition.Kind == Monk.MonkTechniqueKind.HundredFists ||
                definition.Kind == Monk.MonkTechniqueKind.FivePointFocus;
        }

        private static bool SkillRequirementsMet(Skill skill,
            WeaponProfile profile)
        {
            if (skill.Require2H && profile != WeaponProfile.TwoHandMelee)
                return false;
            if (skill.RequireDW && profile != WeaponProfile.DualWield)
                return false;
            if (skill.RequireBow && profile != WeaponProfile.Bow)
                return false;
            if (skill.RequireShield) return false;
            return true;
        }

        private static double SkillPriority(Skill skill)
        {
            double baseValue = skill.PercentDmg > 0f
                ? skill.PercentDmg * 1000.0
                : Math.Max(1, skill.SkillPower) * 100.0;
            return baseValue / Math.Max(0.1, skill.Cooldown / 60.0);
        }

        private static void ApplyPrecombatBuffs(ClassProfile profile,
            List<Spell> spells, RuntimeContext runtime)
        {
            IEnumerable<Spell> buffs = spells.Where(value => value != null &&
                value.Type == Spell.SpellType.Beneficial &&
                value.SpellDurationInTicks > 0 && OffensiveBuffScore(value) > 0)
                .GroupBy(value => value.Line)
                .Select(group => group.OrderByDescending(OffensiveBuffScore)
                    .ThenByDescending(value => value.RequiredLevel).First());
            foreach (Spell buff in buffs)
                GameData.PlayerStats.AddStatusEffectNoChecks(buff, true, 0,
                    GameData.PlayerControl.Myself);

            if (profile.IsNecromancer && runtime.Pet != null)
            {
                Spell petBuff = spells.Where(value =>
                    Necromancer.NecromancerCatalog.TryGetDefinition(value,
                        out Necromancer.NecromancerSpellDefinition definition) &&
                    definition.Kind == Necromancer.NecromancerSpellKind.PetBuff)
                    .OrderByDescending(value => value.RequiredLevel)
                    .FirstOrDefault();
                if (petBuff != null)
                {
                    Stats petStats = runtime.Pet.GetComponent<Stats>();
                    petStats.AddStatusEffectNoChecks(petBuff, true, 0,
                        GameData.PlayerControl.Myself);
                    petStats.CalcStats();
                    petStats.CurrentHP = petStats.CurrentMaxHP;
                }
            }
        }

        private static int OffensiveBuffScore(Spell spell)
        {
            int score = Mathf.Max(0, spell.Str) * 4 +
                Mathf.Max(0, spell.Dex) * 4 + Mathf.Max(0, spell.Int) * 4 +
                Mathf.RoundToInt(Mathf.Max(0f, spell.Haste) * 5f) +
                Mathf.Max(0, spell.AtkRollModifier) * 3;
            switch (spell.Line)
            {
                case Spell.SpellLine.Aura_Arcanist:
                case Spell.SpellLine.Aura_Druid:
                case Spell.SpellLine.Aura_Duelist:
                case Spell.SpellLine.Aura_Paladin:
                case Spell.SpellLine.Aura_Stormcaller:
                case Spell.SpellLine.Aura_Reaver:
                case Spell.SpellLine.Attack_Buff:
                case Spell.SpellLine.Combat_Buff:
                case Spell.SpellLine.DPS_Buff:
                case Spell.SpellLine.Duelist_Attack_Buff:
                case Spell.SpellLine.Duelist_Combat_Buff:
                case Spell.SpellLine.Vithean_Buff:
                case Spell.SpellLine.Reav_Suffering:
                    score += spell.RequiredLevel + 1;
                    break;
            }
            return score;
        }

        private static IEnumerator SummonPet(ClassProfile profile,
            List<Spell> spells, RuntimeContext runtime)
        {
            List<Spell> petSpells = spells.Where(value => value != null &&
                    value.Type == Spell.SpellType.Pet &&
                    value.PetToSummon != null)
                .OrderByDescending(value => value.RequiredLevel)
                .ToList();
            if (petSpells.Count == 0) yield break;

            CastSpell caster = GameData.PlayerControl.Myself.MySpells;
            int maximumMana = GameData.PlayerStats.GetCurrentMaxMana();
            Spell petSpell = petSpells.FirstOrDefault(value =>
                value.ManaCost <= maximumMana);
            if (petSpell == null)
                throw new InvalidOperationException(
                    $"{profile.Name} cannot afford any learned pre-raid pet " +
                    $"with its L{_activeLevel} benchmark loadout (max mana " +
                    $"{maximumMana}; cheapest cost " +
                    $"{petSpells.Min(value => value.ManaCost)})");
            runtime.PetSummonCost = petSpell.ManaCost;
            runtime.PetSummonMaximumMana = maximumMana;
            runtime.PetSummonAffordable = petSpell.ManaCost <=
                runtime.PetSummonMaximumMana;
            SkillsPlugin.Log.LogInfo(
                $"[DPS] {profile.Name} precombat pet {petSpell.SpellName}: " +
                $"cost={runtime.PetSummonCost}, maxMana=" +
                $"{runtime.PetSummonMaximumMana}, affordable=" +
                $"{runtime.PetSummonAffordable}, cast=" +
                $"{petSpell.SpellChargeTime / 60f:F2}s");
            GameData.PlayerStats.CurrentMana = maximumMana;
            if (!caster.StartSpell(petSpell, GameData.PlayerStats, 0f))
                throw new InvalidOperationException(
                    "could not summon the standardized " + profile.Name +
                    " pet with " + petSpell.SpellName);
            float deadline = Time.time + 60f;
            while ((caster.isCasting() ||
                GameData.PlayerControl.Myself.MyCharmedNPC == null) &&
                Time.time < deadline)
                yield return null;
            NPC pet = GameData.PlayerControl.Myself.MyCharmedNPC;
            if (pet == null)
                throw new InvalidOperationException(
                    profile.Name + " pet did not appear after " +
                    petSpell.SpellName);
            for (int i = 0; i < 8; i++) yield return null;
            runtime.Pet = pet;
            pet.AggroOn(runtime.Dummy);

            if (profile.IsNecromancer)
            {
                Necromancer.NecromancerPetMarker marker =
                    pet.GetComponent<Necromancer.NecromancerPetMarker>();
                int expectedTier = 0;
                if (Necromancer.NecromancerCatalog.TryGetDefinition(petSpell,
                        out Necromancer.NecromancerSpellDefinition definition))
                    expectedTier = definition.PetTier;
                if (marker == null || marker.Tier != expectedTier)
                    throw new InvalidOperationException(
                        $"expected tier-{expectedTier} Bone Thrall at " +
                        $"level {_activeLevel}, got {marker?.Tier ?? 0}");
                Item weapon = CreateLevelAppropriatePetWeapon(runtime,
                    marker.Tier);
                string selectedWeapon = runtime.PetWeaponDescription;
                if (!Necromancer.NecromancerPets
                    .ApplyTransientBenchmarkWeapon(pet, weapon, 0,
                        weapon.ItemLevel,
                        out string detail))
                    throw new InvalidOperationException(
                        "could not equip the transient level-appropriate Bone Thrall " +
                        "weapon: " + detail);
                runtime.PetWeaponDescription = selectedWeapon + "; " + detail;
            }
            GameData.PlayerStats.CurrentMana =
                GameData.PlayerStats.GetCurrentMaxMana();
        }

        private static IEnumerator SpawnDummy(RuntimeContext runtime)
        {
            GameObject prefab = Resources.Load<GameObject>("NPCs/TestDummy");
            if (prefab == null)
                throw new InvalidOperationException(
                    "native Resources/NPCs/TestDummy prefab is unavailable");
            Transform player = GameData.PlayerControl.transform;
            Vector3 position = player.position + player.forward * 2f;
            runtime.DummyObject = UnityEngine.Object.Instantiate(prefab,
                position, player.rotation);
            runtime.DummyObject.name =
                $"Classic Skills L{_activeLevel} Training Dummy";
            yield return null;
            yield return null;

            runtime.Dummy = runtime.DummyObject.GetComponent<Character>();
            runtime.DummyStats = runtime.DummyObject.GetComponent<Stats>();
            NPC npc = runtime.DummyObject.GetComponent<NPC>();
            TestDummy nativeRecorder =
                runtime.DummyObject.GetComponent<TestDummy>();
            if (runtime.Dummy == null || runtime.DummyStats == null || npc == null)
                throw new InvalidOperationException(
                    "native training dummy is missing Character, Stats, or NPC");
            if (nativeRecorder != null) nativeRecorder.enabled = false;
            npc.enabled = false;
            npc.Mobile = false;
            npc.NeverAggro = true;
            npc.HandSetResistances = true;
            npc.HardSetAC = ActiveDummyArmor;
            npc.CurrentAggroTarget = null;
            NavMeshAgent nav = runtime.DummyObject.GetComponent<NavMeshAgent>();
            if (nav != null && nav.enabled) nav.enabled = false;

            runtime.Dummy.Alive = true;
            runtime.Dummy.Invulnerable = false;
            runtime.Dummy.DPSDummy = false;
            runtime.Dummy.MyFaction = Character.Faction.OtherEvil;
            runtime.Dummy.BaseFaction = Character.Faction.OtherEvil;
            runtime.Dummy.TempFaction = Character.Faction.OtherEvil;
            runtime.Dummy.NearbyEnemies?.Clear();
            runtime.Dummy.NearbyEnemies?.Add(GameData.PlayerControl.Myself);
            runtime.DummyStats.Level = _activeLevel;
            runtime.DummyStats.CharacterClass = GameData.ClassDB.Default;
            runtime.DummyStats.OverrideHPforNPC = true;
            runtime.DummyStats.BaseHP = DummyHealth;
            runtime.DummyStats.BaseAC = ActiveDummyArmor;
            runtime.DummyStats.BaseMR = ActiveDummyResist;
            runtime.DummyStats.BaseER = ActiveDummyResist;
            runtime.DummyStats.BasePR = ActiveDummyResist;
            runtime.DummyStats.BaseVR = ActiveDummyResist;
            runtime.DummyStats.StopAllRegen = true;
            ClearStatuses(runtime.DummyStats);
            runtime.DummyStats.CalcStats();
            runtime.DummyStats.StopAllRegen = true;
            runtime.DummyStats.CurrentAC = ActiveDummyArmor;
            runtime.DummyStats.CurrentHP = DummyHealth;
            EnsureCombatRange(runtime.Dummy);
            for (int i = 0; i < 3; i++) yield return null;
        }

        private static void EnsureCombatRange(Character target)
        {
            if (target == null) return;
            List<Character> melee = GameData.PlayerCombat?.MyMeleeRange?
                .GetNPCsInRange();
            if (melee != null && !melee.Contains(target)) melee.Add(target);
            List<Character> nearby = GameData.PlayerControl?.Myself?.NearbyEnemies;
            if (nearby != null && !nearby.Contains(target)) nearby.Add(target);
            if (target.NearbyEnemies != null &&
                !target.NearbyEnemies.Contains(GameData.PlayerControl.Myself))
                target.NearbyEnemies.Add(GameData.PlayerControl.Myself);
        }

        private static void ConfigurePlayer(ClassProfile profile,
            bool maxAscension, PlayerState original, RuntimeContext runtime)
        {
            Stats stats = GameData.PlayerStats;
            Inventory inventory = GameData.PlayerInv;
            Character player = GameData.PlayerControl.Myself;
            CastSpell caster = player.MySpells;
            UseSkill skills = player.MySkills;
            GameData.PlayerCombat.ForceAttackOff();
            caster.InterruptCast();
            CleanupExistingPet();
            ClearStatuses(stats);

            stats.CharacterClass = profile.Class;
            stats.Level = _activeLevel;
            stats.BaseHP = _standardBaseHp;
            stats.BaseAC = 0;
            stats.BaseMana = _standardBaseMana;
            stats.BaseStr = original.BaseStr;
            stats.BaseEnd = original.BaseEnd;
            stats.BaseDex = original.BaseDex;
            stats.BaseAgi = original.BaseAgi;
            stats.BaseInt = original.BaseInt;
            stats.BaseWis = original.BaseWis;
            stats.BaseCha = original.BaseCha;
            stats.BaseRes = 0;
            stats.BaseMR = 0;
            stats.BaseER = 0;
            stats.BasePR = 0;
            stats.BaseVR = 0;
            stats.BaseAttackRollModifier = 0;
            stats.BaseLifesteal = 0f;
            stats.BaseMHAtkDelay = 60f;
            stats.BaseOHAtkDelay = 60f;
            stats.BaseArmorPenPercentage = 0f;
            stats.StrScaleSpent = 0;
            stats.EndScaleSpent = 0;
            stats.DexScaleSpent = 0;
            stats.AgiScaleSpent = 0;
            stats.IntScaleSpent = 0;
            stats.WisScaleSpent = 0;
            stats.ChaScaleSpent = 0;
            stats.StopAllRegen = false;

            skills.KnownSkills = GetSkills(profile).ToList();
            caster.KnownSpells = GetSpells(profile).ToList();
            ConfigureAscensions(profile, maxAscension, skills);
            ConfigureFullLoadout(profile, runtime, inventory);
            AllocateOffensiveProficiencies(profile, stats);
            stats.CombatStance = skills.KnownSkills
                .Where(value => value?.StanceToUse != null)
                .Select(value => value.StanceToUse)
                .Concat(new[] { GameData.SkillDatabase.NormalStance })
                .Where(value => value == null ||
                    (value.SelfDamagePerCast <= 0f &&
                     value.SelfDamagePerAttack <= 0f))
                .OrderByDescending(value => value?.DamageMod ?? 0f)
                .ThenByDescending(value => value?.SpellDamageMod ?? 0f)
                .FirstOrDefault();
            stats.CalcStats();
            stats.CurrentHP = stats.CurrentMaxHP;
            stats.CurrentMana = stats.GetCurrentMaxMana();
            stats.CurrentStamina = stats.CurrentMaxStamina;
            player.Alive = true;
            GameData.PlayerControl.CurrentTarget = null;
            GameData.PlayerTyping = true;
        }

        private static string DescribeCurrentPlayerStats()
        {
            Stats stats = GameData.PlayerStats;
            Inventory inventory = GameData.PlayerInv;
            return $"hp={stats.CurrentMaxHP},mana={stats.GetCurrentMaxMana()}," +
                $"str={stats.GetCurrentStr()},dex={stats.GetCurrentDex()}," +
                $"int={stats.GetCurrentInt()},wis={stats.GetCurrentWis()}," +
                $"cha={stats.GetCurrentCha()},res={stats.GetCurrentRes()}," +
                $"strScale={stats.StrScaleMod},dexScale={stats.DexScaleMod}," +
                $"intScale={stats.IntScaleMod},wisScale={stats.WisScaleMod}," +
                $"chaScale={stats.ChaScaleMod},itemMana={inventory.ItemMana}," +
                $"itemInt={inventory.ItemInt},itemWis={inventory.ItemWis}," +
                $"itemCha={inventory.ItemCha}";
        }

        private static string DescribeActiveEffects()
        {
            Stats stats = GameData.PlayerStats;
            if (stats?.StatusEffects == null) return "none";
            string[] effects = stats.StatusEffects
                .Where(value => value?.Effect != null && value.Duration > 0f)
                .Select(value => (value.Effect.SpellName ?? "unnamed") +
                    "#" + (value.Effect.Id ?? "no-id"))
                .OrderBy(value => value, StringComparer.Ordinal)
                .ToArray();
            return effects.Length == 0 ? "none" : string.Join(", ", effects);
        }

        private static void ValidateComparableConfiguration(ParseResult current,
            List<ParseResult> completed)
        {
            ParseResult prior = completed.FirstOrDefault(value =>
                value.Level == current.Level &&
                value.ClassName == current.ClassName &&
                value.Scenario == current.Scenario &&
                value.GearPower == current.GearPower &&
                value.DurationSeconds == current.DurationSeconds &&
                Mathf.Approximately(value.TimeScale, current.TimeScale));
            if (prior == null) return;

            var differences = new List<string>();
            if (!string.Equals(prior.Loadout, current.Loadout,
                    StringComparison.Ordinal))
                differences.Add("loadout");
            if (!string.Equals(prior.Stats, current.Stats,
                    StringComparison.Ordinal))
                differences.Add("stats");
            if (!string.Equals(prior.ActiveEffects, current.ActiveEffects,
                    StringComparison.Ordinal))
                differences.Add("active effects");
            if (!string.Equals(prior.Weapon, current.Weapon,
                    StringComparison.Ordinal))
                differences.Add("weapon");
            if (!string.Equals(prior.PetWeapon, current.PetWeapon,
                    StringComparison.Ordinal))
                differences.Add("pet weapon");
            if (differences.Count == 0) return;

            throw new InvalidOperationException(
                $"{current.ClassName} L{current.Level} repetition " +
                $"configuration changed in {string.Join(", ", differences)}; " +
                $"prior stats=[{prior.Stats}] effects=[{prior.ActiveEffects}], " +
                $"current stats=[{current.Stats}] effects=" +
                $"[{current.ActiveEffects}]");
        }

        private static void ConfigureAscensions(ClassProfile profile,
            bool maximum, UseSkill skills)
        {
            skills.MyAscensions.Clear();
            skills.AscensionPoints = 0;
            if (!maximum) return;
            IEnumerable<Ascension> perks;
            if (profile.IsNecromancer)
                perks = Necromancer.NecromancerAscensions.All;
            else if (profile.IsMonk)
                perks = Monk.MonkAscensions.All;
            else
            {
                Ascension.Class usedBy = NativeAscensionClass(profile.Name);
                perks = GameData.SkillDatabase.AscensionDatabase.Where(value =>
                    value != null && value.UsedBy == usedBy);
            }
            foreach (Ascension perk in perks)
                skills.MyAscensions.Add(new AscensionSkillEntry(perk.Id,
                    perk.MaxRank));
        }

        private static Ascension.Class NativeAscensionClass(string name)
        {
            switch (name)
            {
                case "Arcanist": return Ascension.Class.Arcanist;
                case "Druid": return Ascension.Class.Druid;
                case "Duelist": return Ascension.Class.Duelist;
                case "Paladin": return Ascension.Class.Paladin;
                case "Stormcaller": return Ascension.Class.Stormcaller;
                case "Reaver": return Ascension.Class.Reaver;
                default: return Ascension.Class.General;
            }
        }

        private static void AllocateOffensiveProficiencies(
            ClassProfile profile, Stats stats)
        {
            // Exact native Stats.CalcStats progression: 10 starting points,
            // then one at level 2 and every two levels thereafter (27 at 35).
            int points = 10 + Math.Max(0,
                Mathf.FloorToInt((_activeLevel - 2) / 2f) + 1);
            if (profile.Weapon == WeaponProfile.Wand)
            {
                int intelligenceCap = Math.Max(0, 40 -
                    profile.Class.IntBenefit - stats.MyInv.IntScaleMod);
                stats.IntScaleSpent = Math.Min(points, intelligenceCap);
                int remaining = points - stats.IntScaleSpent;
                int charismaCap = Math.Max(0, 40 -
                    profile.Class.ChaBenefit - stats.MyInv.ChaScaleMod);
                stats.ChaScaleSpent = Math.Min(remaining, charismaCap);
                stats.WisScaleSpent = remaining - stats.ChaScaleSpent;
                return;
            }
            double best = double.MinValue;
            int bestStr = 0;
            for (int str = 0; str <= points; str++)
            {
                int dex = points - str;
                double strScale = Math.Min(40, profile.Class.StrBenefit +
                    stats.MyInv.StrScaleMod + str) / 100.0;
                double dexScale = Math.Min(40, profile.Class.DexBenefit +
                    stats.MyInv.DexScaleMod + dex) / 100.0;
                double value = profile.Weapon == WeaponProfile.Bow
                    ? stats.BaseStr * strScale * 1.5 +
                      stats.BaseDex * dexScale * 0.4
                    : stats.BaseStr * strScale *
                      (profile.Weapon == WeaponProfile.TwoHandMelee ? 1.1 : 1.0) +
                      stats.BaseDex * dexScale * 0.5;
                if (value > best)
                {
                    best = value;
                    bestStr = str;
                }
            }
            stats.StrScaleSpent = bestStr;
            stats.DexScaleSpent = points - bestStr;
        }

        private static void ConfigureFullLoadout(ClassProfile profile,
            RuntimeContext runtime, Inventory inventory)
        {
            if (inventory.EquipmentSlots == null ||
                inventory.EquipmentSlots.Count == 0)
                throw new InvalidOperationException(
                    "the native equipment slot collection is unavailable");

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

            var usedUniqueIds = new HashSet<string>(StringComparer.Ordinal);
            var descriptions = new List<string>();
            ItemIcon main = inventory.MH ?? inventory.EquipmentSlots
                .First(value => value != null &&
                    value.ThisSlotType == Item.SlotType.Primary);
            ItemIcon off = inventory.OH ?? inventory.EquipmentSlots
                .First(value => value != null &&
                    value.ThisSlotType == Item.SlotType.Secondary);
            Item primary = SelectBestWeapon(profile, false, usedUniqueIds);
            EquipBenchmarkItem(profile, runtime, main, primary,
                usedUniqueIds, descriptions);

            Item secondary = null;
            if (profile.Weapon == WeaponProfile.DualWield)
                secondary = SelectBestWeapon(profile, true, usedUniqueIds);
            else if (profile.Weapon == WeaponProfile.Wand)
                secondary = SelectBestEquipment(profile,
                    Item.SlotType.Secondary, usedUniqueIds,
                    value => value.Shield && value.WeaponDmg <= 0);
            EquipBenchmarkItem(profile, runtime, off, secondary,
                usedUniqueIds, descriptions);

            foreach (ItemIcon slot in inventory.EquipmentSlots)
            {
                if (slot == null || ReferenceEquals(slot, main) ||
                    ReferenceEquals(slot, off)) continue;
                Item selected = SelectBestEquipment(profile,
                    slot.ThisSlotType, usedUniqueIds, null);
                EquipBenchmarkItem(profile, runtime, slot, selected,
                    usedUniqueIds, descriptions);
            }
            if (inventory.AuraSlot != null)
            {
                Item aura = SelectBestEquipment(profile, Item.SlotType.Aura,
                    usedUniqueIds, value => value.Aura != null);
                EquipBenchmarkItem(profile, runtime, inventory.AuraSlot,
                    aura, usedUniqueIds, descriptions);
            }
            if (inventory.CharmSlot != null)
            {
                Item charm = SelectBestEquipment(profile, Item.SlotType.Charm,
                    usedUniqueIds, value => !string.Equals(value.ItemName,
                        "Pupil's Pin", StringComparison.OrdinalIgnoreCase));
                EquipBenchmarkItem(profile, runtime, inventory.CharmSlot,
                    charm, usedUniqueIds, descriptions);
            }

            inventory.UpdatePlayerInventory();
            GameData.PlayerStats.MyAura = null;
            GameData.PlayerStats.CheckAuras();
            runtime.WeaponDescription = DescribeWeapon(primary, secondary,
                profile.Weapon == WeaponProfile.DualWield);
            runtime.LoadoutDescription = descriptions.Count == 0
                ? "none" : string.Join("; ", descriptions);
            SkillsPlugin.Log.LogInfo($"[DPS] L{_activeLevel} " +
                $"{profile.Name} {_activeGearPower} gear ceiling " +
                $"L{ActiveGearLevelCeiling} loadout: " +
                runtime.LoadoutDescription);
        }

        private static void EquipBenchmarkItem(ClassProfile profile,
            RuntimeContext runtime, ItemIcon slot, Item source,
            HashSet<string> usedUniqueIds, List<string> descriptions)
        {
            if (slot == null) return;
            if (source == null)
            {
                slot.MyItem = GameData.PlayerInv.Empty;
                slot.Quantity = 1;
                return;
            }
            Item clone = UnityEngine.Object.Instantiate(source);
            clone.name = $"DPS Loadout L{_activeLevel} {profile.Name} " +
                $"{_activeGearPower} {slot.ThisSlotType} - {source.ItemName}";
            if (profile.IsNecromancer && clone.Classes != null &&
                clone.Classes.Count > 0 &&
                !clone.Classes.Contains(profile.Class))
            {
                clone.Classes = new List<Class>(clone.Classes)
                    { profile.Class };
            }
            clone.hideFlags = HideFlags.HideAndDontSave;
            runtime.EquipmentClones.Add(clone);
            slot.MyItem = clone;
            slot.Quantity = 1;
            if (source.Unique) usedUniqueIds.Add(ItemIdentity(source));
            descriptions.Add($"{slot.ThisSlotType}={source.ItemName} " +
                $"(L{source.ItemLevel})");
        }

        private static Item SelectBestEquipment(ClassProfile profile,
            Item.SlotType slotType, HashSet<string> usedUniqueIds,
            Func<Item, bool> additionalFilter)
        {
            return GetBenchmarkItems(profile).Where(value =>
                    IsEligibleBenchmarkItem(value, profile) &&
                    value.RequiredSlot == slotType &&
                    (!value.Unique ||
                     !usedUniqueIds.Contains(ItemIdentity(value))) &&
                    (additionalFilter == null || additionalFilter(value)))
                .OrderByDescending(value => EquipmentScore(value, profile))
                .ThenByDescending(value => value.ItemLevel)
                .ThenBy(value => value.ItemName)
                .FirstOrDefault();
        }

        private static Item SelectBestWeapon(ClassProfile profile,
            bool secondary, HashSet<string> usedUniqueIds)
        {
            IEnumerable<Item> compatible = GetBenchmarkItems(profile)
                .Where(value =>
                IsEligibleBenchmarkItem(value, profile) &&
                value.WeaponDmg > 0 &&
                (!value.Unique ||
                 !usedUniqueIds.Contains(ItemIdentity(value))) &&
                (secondary
                    ? value.RequiredSlot == Item.SlotType.Secondary ||
                      value.RequiredSlot == Item.SlotType.PrimaryOrSecondary
                    : value.RequiredSlot == Item.SlotType.Primary ||
                      value.RequiredSlot == Item.SlotType.PrimaryOrSecondary));
            switch (profile.Weapon)
            {
                case WeaponProfile.Wand:
                    compatible = compatible.Where(value => value.IsWand);
                    break;
                case WeaponProfile.Bow:
                    compatible = compatible.Where(value => value.IsBow ||
                        value.ThisWeaponType == Item.WeaponType.TwoHandBow);
                    break;
                case WeaponProfile.TwoHandMelee:
                    compatible = compatible.Where(value =>
                        value.ThisWeaponType == Item.WeaponType.TwoHandMelee ||
                        value.ThisWeaponType == Item.WeaponType.TwoHandStaff);
                    break;
                case WeaponProfile.DualWield:
                    compatible = compatible.Where(value =>
                        value.ThisWeaponType == Item.WeaponType.OneHandDagger ||
                        value.ThisWeaponType == Item.WeaponType.OneHandMelee);
                    break;
            }
            Item selected = compatible
                .OrderByDescending(value => WeaponScore(value, profile))
                .ThenByDescending(value => value.ItemLevel)
                .ThenBy(value => value.ItemName)
                .FirstOrDefault();
            if (selected == null)
                throw new InvalidOperationException(
                    $"no {profile.Weapon} benchmark weapon at or below gear " +
                    $"ceiling L{ActiveGearLevelCeiling} is compatible with " +
                    profile.Name);
            return selected;
        }

        private static IEnumerable<Item> GetBenchmarkItems(
            ClassProfile profile)
        {
            IEnumerable<Item> items = GameData.ItemDB.ItemDB;
            if (profile.IsMonk)
                items = items.Concat(Monk.MonkGear.AllItems)
                    .Concat(Monk.MonkAuras.AllItems);
            return items.Where(value => value != null).Distinct();
        }

        private static bool IsEligibleBenchmarkItem(Item item,
            ClassProfile profile)
        {
            if (item == null || item == GameData.PlayerInv.Empty ||
                item.ItemLevel < 1 ||
                item.ItemLevel > ActiveGearLevelCeiling ||
                item.RequiredSlot == Item.SlotType.General || item.Stackable ||
                item.Template) return false;
            bool addonItem = (item.Id ?? "").StartsWith("CS_",
                StringComparison.OrdinalIgnoreCase);
            if (addonItem)
            {
                if (!profile.IsMonk) return false;
                bool designedMonkItem = Monk.MonkGear.AllItems.Contains(item) ||
                    Monk.MonkAuras.AllItems.Contains(item);
                return designedMonkItem && item.Classes != null &&
                    item.Classes.Contains(profile.Class);
            }
            if (item.Classes == null || item.Classes.Count == 0 ||
                item.Classes.Contains(profile.Class)) return true;
            return profile.IsNecromancer && item.Classes.Any(value =>
                value == GameData.ClassDB.Arcanist ||
                value == GameData.ClassDB.Druid);
        }

        private static double EquipmentScore(Item item,
            ClassProfile profile)
        {
            double score = item.ItemLevel * 2.0 + item.HP * 0.02 +
                item.AC * 0.10 + item.Mana * 0.03 + item.End * 1.5 +
                item.Agi * 1.0 + item.Wis * 1.0 +
                (item.MR + item.ER + item.PR + item.VR) * 0.25;
            switch (profile.Weapon)
            {
                case WeaponProfile.Wand:
                    score += item.Int * 10.0 + item.Res * 8.0 +
                        item.Cha * 3.0 + item.Str * 0.25 + item.Dex * 0.5;
                    score += item.IntScaling * 100.0 +
                        item.ChaScaling * 30.0 + item.WisScaling * 10.0;
                    break;
                case WeaponProfile.DualWield:
                    score += item.Str * 10.0 + item.Dex * 8.0 +
                        item.Res * 1.0;
                    score += item.StrScaling * 100.0 +
                        item.DexScaling * 70.0;
                    break;
                case WeaponProfile.TwoHandMelee:
                    score += item.Str * 10.0 + item.Dex * 6.0 +
                        item.Int * (profile.Name == "Reaver" ? 2.0 : 0.5) +
                        item.Res * 1.0;
                    score += item.StrScaling * 100.0 +
                        item.DexScaling * 55.0;
                    break;
                case WeaponProfile.Bow:
                    score += item.Str * 8.0 + item.Dex * 5.0 +
                        item.Int * 7.0 + item.Res * 5.0 + item.Cha * 1.5;
                    score += item.StrScaling * 70.0 +
                        item.DexScaling * 45.0 + item.IntScaling * 80.0;
                    break;
            }
            score += BeneficialEffectScore(item.WornEffect, profile);
            score += BeneficialEffectScore(item.Aura, profile);
            return score;
        }

        private static double WeaponScore(Item item, ClassProfile profile)
        {
            double delay = Math.Max(0.1, item.WeaponDly);
            double score = item.WeaponDmg / delay * 100.0 +
                EquipmentScore(item, profile);
            score += ProcScore(item.WeaponProcOnHit,
                item.WeaponProcChance, delay);
            score += ProcScore(item.WandEffect,
                item.WandProcChance, delay);
            score += ProcScore(item.BowEffect,
                item.BowProcChance, delay);
            return score;
        }

        private static double ProcScore(Spell spell, float chance,
            double weaponDelay)
        {
            if (spell == null || chance <= 0f) return 0.0;
            double ticks = spell.Type == Spell.SpellType.StatusEffect
                ? Math.Sqrt(Math.Max(1, spell.SpellDurationInTicks)) : 1.0;
            double damage = Math.Max(0, spell.TargetDamage) * ticks;
            if (spell.StatusEffectToApply != null)
                damage += Math.Max(0,
                    spell.StatusEffectToApply.TargetDamage) *
                    Math.Sqrt(Math.Max(1,
                        spell.StatusEffectToApply.SpellDurationInTicks));
            return damage * (chance / 100.0) / weaponDelay * 3.0;
        }

        private static double BeneficialEffectScore(Spell spell,
            ClassProfile profile)
        {
            if (spell == null) return 0.0;
            double score = Math.Max(0f, spell.Haste) * 8.0 +
                Math.Max(0, spell.AtkRollModifier) * 4.0 +
                Math.Max(0, spell.ArmorPenPercent) * 10.0;
            if (profile.Weapon == WeaponProfile.Wand)
                score += Math.Max(0, spell.Int) * 10.0 +
                    Math.Max(0, spell.Cha) * 3.0;
            else if (profile.Weapon == WeaponProfile.Bow)
                score += Math.Max(0, spell.Str) * 8.0 +
                    Math.Max(0, spell.Dex) * 5.0 +
                    Math.Max(0, spell.Int) * 7.0;
            else
                score += Math.Max(0, spell.Str) * 10.0 +
                    Math.Max(0, spell.Dex) * 6.0;
            if (spell.AddProc != null && spell.AddProcChance > 0)
                score += ProcScore(spell.AddProc, spell.AddProcChance, 1.0);
            return score;
        }

        private static string DescribeWeapon(Item primary, Item secondary,
            bool dualWield)
        {
            if (primary == null) return "none";
            string output = $"{primary.ItemName} " +
                $"({primary.WeaponDmg} dmg/{primary.WeaponDly:F2}s, L" +
                $"{primary.ItemLevel})";
            if (dualWield && secondary != null)
                output += $" + {secondary.ItemName} " +
                    $"({secondary.WeaponDmg} dmg/" +
                    $"{secondary.WeaponDly:F2}s, L{secondary.ItemLevel})";
            return output;
        }

        private static string ItemIdentity(Item item)
        {
            return string.IsNullOrWhiteSpace(item?.Id)
                ? item?.ItemName ?? "unnamed" : item.Id;
        }

        private static Item CreateLevelAppropriatePetWeapon(
            RuntimeContext runtime, int tier)
        {
            Class necromancer = Necromancer.NecromancerCatalog.NecromancerClass;
            int targetLevel = _activeLevel == MaximumLevel &&
                _activeGearPower == GearPower.Optimized
                ? 32 : Math.Max(1, _activeLevel - 3);
            targetLevel = Math.Min(targetLevel, ActiveGearLevelCeiling);
            List<Item> candidates = GameData.ItemDB.ItemDB.Where(value =>
                    value != null && value.WeaponDmg > 0 &&
                    !(value.Id ?? "").StartsWith("CS_",
                        StringComparison.OrdinalIgnoreCase) &&
                    (value.ThisWeaponType == Item.WeaponType.OneHandMelee ||
                     value.ThisWeaponType == Item.WeaponType.OneHandDagger) &&
                    (value.RequiredSlot == Item.SlotType.Primary ||
                     value.RequiredSlot == Item.SlotType.PrimaryOrSecondary) &&
                    !value.Shield && value.ItemLevel >= 1 &&
                    value.ItemLevel <= ActiveGearLevelCeiling &&
                    HasNativeWeaponVisual(value))
                .ToList();
            if (candidates.Count == 0)
                throw new InvalidOperationException(
                    $"no native one-handed weapon at or below level " +
                    $"{ActiveGearLevelCeiling} has a usable pet visual");

            int nearestDistance = candidates.Min(value =>
                Math.Abs(value.ItemLevel - targetLevel));
            Item source = candidates.Where(value =>
                    Math.Abs(value.ItemLevel - targetLevel) == nearestDistance)
                .OrderByDescending(PetWeaponScore)
                .ThenBy(value => value.ItemName).First();
            Item clone = UnityEngine.Object.Instantiate(source);
            clone.name = $"CS DPS Bone Thrall L{targetLevel} Weapon";
            clone.ItemName = $"Level {targetLevel} " + source.ItemName;
            clone.Id = $"CS_DPS_BONE_THRALL_WEAPON_" +
                $"{_activeGearPower.ToString().ToUpperInvariant()}_L" +
                targetLevel;
            clone.ItemLevel = targetLevel;
            clone.RequiredSlot = Item.SlotType.Primary;
            clone.Classes = new List<Class> { necromancer };
            clone.hideFlags = HideFlags.HideAndDontSave;
            runtime.PetWeaponClone = clone;
            runtime.PetWeaponDescription =
                $"tier-{tier} {clone.ItemName} " +
                $"({clone.WeaponDmg} dmg/{clone.WeaponDly:F2}s, " +
                $"visual {clone.EquipmentToActivate}, normalized from native " +
                $"{source.ItemName} L{source.ItemLevel})";
            return clone;
        }

        private static double PetWeaponScore(Item item)
        {
            double delay = Math.Max(0.1, item.WeaponDly);
            return item.WeaponDmg / delay * 100.0 + item.Str * 10.0 +
                item.Dex * 6.0 + item.HP * 0.01 + item.AC * 0.05 +
                ProcScore(item.WeaponProcOnHit, item.WeaponProcChance, delay);
        }

        private static bool HasNativeWeaponVisual(Item item)
        {
            Transform root = GameData.PlayerModularParent?.Male?.WeaponR;
            return root != null && !string.IsNullOrWhiteSpace(
                    item?.EquipmentToActivate) &&
                root.GetComponentsInChildren<Transform>(true).Any(value =>
                    value != root && string.Equals(value.name,
                        item.EquipmentToActivate, StringComparison.Ordinal));
        }

        private static IEnumerable<Spell> GetSpells(ClassProfile profile)
        {
            if (profile.IsNecromancer)
                return Necromancer.NecromancerCatalog.GetLearnableSpells()
                    .Where(value => value.RequiredLevel <= _activeLevel)
                    .OrderBy(value => value.RequiredLevel).ToArray();
            if (profile.IsMonk) return Array.Empty<Spell>();
            return GameData.SpellDatabase.SpellDatabase.Where(value =>
                    value != null && value.RequiredLevel > 0 &&
                    value.RequiredLevel <= _activeLevel &&
                    value.UsedBy != null && value.UsedBy.Contains(profile.Class) &&
                    PreRaidReferenceCatalog.Allows(value))
                .OrderBy(value => value.RequiredLevel).ToArray();
        }

        private static IEnumerable<Skill> GetSkills(ClassProfile profile)
        {
            if (profile.IsNecromancer) return Array.Empty<Skill>();
            if (profile.IsMonk)
            {
                IEnumerable<Skill> custom = Monk.MonkCatalog.AllSkills
                    .Where(value =>
                        Monk.MonkCatalog.TryGetDefinition(value,
                            out Monk.MonkTechniqueDefinition definition) &&
                        definition.Level <= _activeLevel);
                IEnumerable<Skill> shared = Monk.MonkSharedSkills.All
                    .Where(value => value.RequiredLevel <= _activeLevel)
                    .Select(value => GameData.SkillDatabase
                        .GetSkillByID(value.SkillId))
                    .Where(value => value != null);
                return custom.Concat(shared).Distinct().ToArray();
            }
            return GameData.SkillDatabase.SkillDatabase.Where(value =>
                value != null && RequiredSkillLevel(value, profile.Name) > 0 &&
                RequiredSkillLevel(value, profile.Name) <= _activeLevel &&
                PreRaidReferenceCatalog.Allows(value));
        }

        private static int RequiredSkillLevel(Skill skill, string className)
        {
            switch (className)
            {
                case "Arcanist": return skill.ArcanistRequiredLevel;
                case "Druid": return skill.DruidRequiredLevel;
                case "Duelist": return skill.DuelistRequiredLevel;
                case "Paladin": return skill.PaladinRequiredLevel;
                case "Stormcaller": return skill.StormcallerRequiredLevel;
                case "Reaver": return skill.ReaverRequiredLevel;
                default: return 0;
            }
        }

        private static List<ClassProfile> BuildProfiles(bool includeMonk)
        {
            var output = new List<ClassProfile>
            {
                new ClassProfile { Name = "Arcanist", Class = GameData.ClassDB.Arcanist, Weapon = WeaponProfile.Wand },
                new ClassProfile { Name = "Druid", Class = GameData.ClassDB.Druid, Weapon = WeaponProfile.Wand },
                new ClassProfile { Name = "Duelist", Class = GameData.ClassDB.Duelist, Weapon = WeaponProfile.DualWield },
                new ClassProfile { Name = "Paladin", Class = GameData.ClassDB.Paladin, Weapon = WeaponProfile.TwoHandMelee },
                new ClassProfile { Name = "Stormcaller", Class = GameData.ClassDB.Stormcaller, Weapon = WeaponProfile.Bow },
                new ClassProfile { Name = "Reaver", Class = GameData.ClassDB.Reaver, Weapon = WeaponProfile.TwoHandMelee },
                new ClassProfile { Name = "Necromancer", Class = Necromancer.NecromancerCatalog.NecromancerClass, Weapon = WeaponProfile.Wand, IsNecromancer = true }
            };
            if (includeMonk)
                output.Add(new ClassProfile
                {
                    Name = "Monk",
                    Class = Monk.MonkCatalog.MonkClass,
                    Weapon = WeaponProfile.DualWield,
                    IsMonk = true
                });
            if (output.Any(value => value.Class == null))
                throw new InvalidOperationException(
                    "one or more class database entries are unavailable");
            return output;
        }

        private static PlayerState CapturePlayerState()
        {
            Stats stats = GameData.PlayerStats;
            Character player = GameData.PlayerControl.Myself;
            UseSkill skills = player.MySkills;
            CastSpell spells = player.MySpells;
            var state = new PlayerState
            {
                CharacterClass = stats.CharacterClass,
                Level = stats.Level,
                CurrentExperience = stats.CurrentExperience,
                CurrentAscensionXp = stats.CurrentAscensionXP,
                BaseHp = stats.BaseHP,
                BaseAc = stats.BaseAC,
                BaseMana = stats.BaseMana,
                BaseStr = stats.BaseStr,
                BaseEnd = stats.BaseEnd,
                BaseDex = stats.BaseDex,
                BaseAgi = stats.BaseAgi,
                BaseInt = stats.BaseInt,
                BaseWis = stats.BaseWis,
                BaseCha = stats.BaseCha,
                BaseRes = stats.BaseRes,
                BaseMr = stats.BaseMR,
                BaseEr = stats.BaseER,
                BasePr = stats.BasePR,
                BaseVr = stats.BaseVR,
                BaseAttackRoll = stats.BaseAttackRollModifier,
                BaseLifesteal = stats.BaseLifesteal,
                BaseMainDelay = stats.BaseMHAtkDelay,
                BaseOffDelay = stats.BaseOHAtkDelay,
                BaseArmorPen = stats.BaseArmorPenPercentage,
                CurrentHp = stats.CurrentHP,
                CurrentMana = stats.CurrentMana,
                CurrentStamina = stats.CurrentStamina,
                StrSpent = stats.StrScaleSpent,
                EndSpent = stats.EndScaleSpent,
                DexSpent = stats.DexScaleSpent,
                AgiSpent = stats.AgiScaleSpent,
                IntSpent = stats.IntScaleSpent,
                WisSpent = stats.WisScaleSpent,
                ChaSpent = stats.ChaScaleSpent,
                CombatStance = stats.CombatStance,
                StopAllRegen = stats.StopAllRegen,
                Aura = stats.MyAura,
                Position = GameData.PlayerControl.transform.position,
                Rotation = GameData.PlayerControl.transform.rotation,
                Target = GameData.PlayerControl.CurrentTarget,
                PlayerTyping = GameData.PlayerTyping,
                AutoEngage = GameData.AutoEngageAttackOnSkill,
                InCombat = GameData.InCombat,
                Autoattacking = GameData.Autoattacking,
                TimeScale = Time.timeScale,
                RandomState = UnityEngine.Random.state,
                AscensionPoints = skills.AscensionPoints,
                Inventory = CaptureInventoryState(GameData.PlayerInv)
            };
            if (spells.KnownSpells != null)
                state.KnownSpells.AddRange(spells.KnownSpells);
            if (skills.KnownSkills != null)
                state.KnownSkills.AddRange(skills.KnownSkills);
            if (skills.MyAscensions != null)
                foreach (AscensionSkillEntry entry in skills.MyAscensions)
                    if (entry != null)
                        state.Ascensions.Add(new AscensionSkillEntry(entry.id,
                            entry.level));
            if (GameData.HKMngr?.AllHotkeys != null)
                state.HotkeyCooldowns.AddRange(GameData.HKMngr.AllHotkeys
                    .Select(value => value?.Cooldown ?? 0f));
            foreach (StatusEffect status in stats.StatusEffects)
                state.Statuses.Add(new StatusState
                {
                    Effect = status?.Effect,
                    Duration = status?.Duration ?? 0f,
                    FromPlayer = status?.fromPlayer ?? false,
                    BonusDamage = status?.bonusDmg ?? 0,
                    CastedByPc = status?.CastedByPC ?? false,
                    Owner = status?.Owner,
                    CreditDps = status?.CreditDPS
                });
            return state;
        }

        private static InventoryState CaptureInventoryState(Inventory value)
        {
            var state = new InventoryState
            {
                MainHand = value.MH?.MyItem,
                MainHandQuantity = value.MH?.Quantity ?? 1,
                OffHand = value.OH?.MyItem,
                OffHandQuantity = value.OH?.Quantity ?? 1,
                ItemHp = value.ItemHP,
                ItemAc = value.ItemAC,
                ItemMana = value.ItemMana,
                ItemStr = value.ItemStr,
                ItemEnd = value.ItemEnd,
                ItemDex = value.ItemDex,
                ItemAgi = value.ItemAgi,
                ItemInt = value.ItemInt,
                ItemWis = value.ItemWis,
                ItemCha = value.ItemCha,
                ItemRes = value.ItemRes,
                ItemMr = value.ItemMR,
                ItemEr = value.ItemER,
                ItemPr = value.ItemPR,
                ItemVr = value.ItemVR,
                MainHandDelay = value.MHDelay,
                MainHandDamage = value.MHDmg,
                OffHandDamage = value.OHDmg,
                OffHandDelay = value.OHDelay,
                MainHandLevel = value.MHLevel,
                SecondaryWeapon = value.SecondaryWeapon,
                PrimaryWeapon = value.PrimaryWeapon,
                TwoHandPrimary = value.TwoHandPrimary,
                SecondaryShield = value.SecondaryShield,
                PrimaryBow = value.PrimaryBow,
                StrScaleMod = value.StrScaleMod,
                EndScaleMod = value.EndScaleMod,
                DexScaleMod = value.DexScaleMod,
                AgiScaleMod = value.AgiScaleMod,
                IntScaleMod = value.IntScaleMod,
                WisScaleMod = value.WisScaleMod,
                ChaScaleMod = value.ChaScaleMod,
                MitScaleMod = value.MitScaleMod,
                RstScaleMod = value.RstScaleMod
            };
            state.WornEffects.AddRange(value.WornEffects);
            if (value.EquipmentSlots != null)
                foreach (ItemIcon slot in value.EquipmentSlots)
                    CaptureEquipmentSlot(state, slot);
            CaptureEquipmentSlot(state, value.AuraSlot);
            CaptureEquipmentSlot(state, value.CharmSlot);
            return state;
        }

        private static void CaptureEquipmentSlot(InventoryState state,
            ItemIcon slot)
        {
            if (slot == null || state.Slots.Any(value =>
                    ReferenceEquals(value.Slot, slot))) return;
            state.Slots.Add(new EquipmentSlotState
            {
                Slot = slot,
                Item = slot.MyItem,
                Quantity = slot.Quantity
            });
        }

        private static void RestorePlayerState(PlayerState state)
        {
            CleanupExistingPet();
            GameData.PlayerCombat?.ForceAttackOff();
            GameData.PlayerControl?.Myself?.MySpells?.InterruptCast();
            Stats stats = GameData.PlayerStats;
            Character player = GameData.PlayerControl.Myself;
            UseSkill skills = player.MySkills;
            stats.CharacterClass = state.CharacterClass;
            stats.Level = state.Level;
            stats.CurrentExperience = state.CurrentExperience;
            stats.CurrentAscensionXP = state.CurrentAscensionXp;
            stats.BaseHP = state.BaseHp;
            stats.BaseAC = state.BaseAc;
            stats.BaseMana = state.BaseMana;
            stats.BaseStr = state.BaseStr;
            stats.BaseEnd = state.BaseEnd;
            stats.BaseDex = state.BaseDex;
            stats.BaseAgi = state.BaseAgi;
            stats.BaseInt = state.BaseInt;
            stats.BaseWis = state.BaseWis;
            stats.BaseCha = state.BaseCha;
            stats.BaseRes = state.BaseRes;
            stats.BaseMR = state.BaseMr;
            stats.BaseER = state.BaseEr;
            stats.BasePR = state.BasePr;
            stats.BaseVR = state.BaseVr;
            stats.BaseAttackRollModifier = state.BaseAttackRoll;
            stats.BaseLifesteal = state.BaseLifesteal;
            stats.BaseMHAtkDelay = state.BaseMainDelay;
            stats.BaseOHAtkDelay = state.BaseOffDelay;
            stats.BaseArmorPenPercentage = state.BaseArmorPen;
            stats.StrScaleSpent = state.StrSpent;
            stats.EndScaleSpent = state.EndSpent;
            stats.DexScaleSpent = state.DexSpent;
            stats.AgiScaleSpent = state.AgiSpent;
            stats.IntScaleSpent = state.IntSpent;
            stats.WisScaleSpent = state.WisSpent;
            stats.ChaScaleSpent = state.ChaSpent;
            stats.CombatStance = state.CombatStance;
            stats.StopAllRegen = state.StopAllRegen;
            RestoreInventoryState(GameData.PlayerInv, state.Inventory);
            player.MySpells.KnownSpells = new List<Spell>(state.KnownSpells);
            skills.KnownSkills = new List<Skill>(state.KnownSkills);
            skills.MyAscensions.Clear();
            skills.MyAscensions.AddRange(state.Ascensions.Select(value =>
                new AscensionSkillEntry(value.id, value.level)));
            skills.AscensionPoints = state.AscensionPoints;
            ClearStatuses(stats);
            int statusCount = Mathf.Min(state.Statuses.Count,
                stats.StatusEffects.Length);
            for (int i = 0; i < statusCount; i++)
            {
                StatusState saved = state.Statuses[i];
                StatusEffect target = stats.StatusEffects[i];
                target.Effect = saved.Effect;
                target.Duration = saved.Duration;
                target.fromPlayer = saved.FromPlayer;
                target.bonusDmg = saved.BonusDamage;
                target.CastedByPC = saved.CastedByPc;
                target.Owner = saved.Owner;
                target.CreditDPS = saved.CreditDps;
            }
            stats.MyAura = state.Aura;
            stats.CalcStats();
            stats.CurrentHP = Mathf.Clamp(state.CurrentHp, 1,
                Mathf.Max(1, stats.CurrentMaxHP));
            stats.CurrentMana = Mathf.Clamp(state.CurrentMana, 0,
                Mathf.Max(0, stats.GetCurrentMaxMana()));
            stats.CurrentStamina = Mathf.Clamp(state.CurrentStamina, 0,
                Mathf.Max(1, stats.CurrentMaxStamina));
            GameData.PlayerControl.transform.position = state.Position;
            GameData.PlayerControl.transform.rotation = state.Rotation;
            GameData.PlayerControl.CurrentTarget = state.Target;
            GameData.PlayerTyping = state.PlayerTyping;
            GameData.AutoEngageAttackOnSkill = state.AutoEngage;
            GameData.InCombat = state.InCombat;
            GameData.Autoattacking = state.Autoattacking;
            Time.timeScale = state.TimeScale;
            UnityEngine.Random.state = state.RandomState;
            if (GameData.HKMngr?.AllHotkeys != null)
                for (int i = 0; i < GameData.HKMngr.AllHotkeys.Count &&
                    i < state.HotkeyCooldowns.Count; i++)
                    if (GameData.HKMngr.AllHotkeys[i] != null)
                        GameData.HKMngr.AllHotkeys[i].Cooldown =
                            state.HotkeyCooldowns[i];
        }

        private static void RestoreInventoryState(Inventory value,
            InventoryState state)
        {
            foreach (EquipmentSlotState saved in state.Slots)
            {
                if (saved?.Slot == null) continue;
                saved.Slot.MyItem = saved.Item ?? value.Empty;
                saved.Slot.Quantity = saved.Quantity;
            }
            if (value.MH != null)
            {
                value.MH.MyItem = state.MainHand ?? value.Empty;
                value.MH.Quantity = state.MainHandQuantity;
            }
            if (value.OH != null)
            {
                value.OH.MyItem = state.OffHand ?? value.Empty;
                value.OH.Quantity = state.OffHandQuantity;
            }
            value.ItemHP = state.ItemHp;
            value.ItemAC = state.ItemAc;
            value.ItemMana = state.ItemMana;
            value.ItemStr = state.ItemStr;
            value.ItemEnd = state.ItemEnd;
            value.ItemDex = state.ItemDex;
            value.ItemAgi = state.ItemAgi;
            value.ItemInt = state.ItemInt;
            value.ItemWis = state.ItemWis;
            value.ItemCha = state.ItemCha;
            value.ItemRes = state.ItemRes;
            value.ItemMR = state.ItemMr;
            value.ItemER = state.ItemEr;
            value.ItemPR = state.ItemPr;
            value.ItemVR = state.ItemVr;
            value.MHDelay = state.MainHandDelay;
            value.MHDmg = state.MainHandDamage;
            value.OHDmg = state.OffHandDamage;
            value.OHDelay = state.OffHandDelay;
            value.MHLevel = state.MainHandLevel;
            value.SecondaryWeapon = state.SecondaryWeapon;
            value.PrimaryWeapon = state.PrimaryWeapon;
            value.TwoHandPrimary = state.TwoHandPrimary;
            value.SecondaryShield = state.SecondaryShield;
            value.PrimaryBow = state.PrimaryBow;
            value.StrScaleMod = state.StrScaleMod;
            value.EndScaleMod = state.EndScaleMod;
            value.DexScaleMod = state.DexScaleMod;
            value.AgiScaleMod = state.AgiScaleMod;
            value.IntScaleMod = state.IntScaleMod;
            value.WisScaleMod = state.WisScaleMod;
            value.ChaScaleMod = state.ChaScaleMod;
            value.MitScaleMod = state.MitScaleMod;
            value.RstScaleMod = state.RstScaleMod;
            value.WornEffects.Clear();
            value.WornEffects.AddRange(state.WornEffects);
            value.UpdatePlayerInventory();
        }

        private static void CleanupRuntime(RuntimeContext runtime)
        {
            EndAttribution(null);
            Necromancer.NecromancerRuntime.CancelCovenantForTesting(
                GameData.PlayerStats);
            GameData.PlayerCombat?.ForceAttackOff();
            GameData.PlayerControl?.Myself?.MySpells?.InterruptCast();
            if (runtime != null)
            {
                if (runtime.Pet != null)
                {
                    if (GameData.PlayerControl?.Myself?.MyCharmedNPC == runtime.Pet)
                        GameData.PlayerControl.Myself.MyCharmedNPC = null;
                    UnityEngine.Object.Destroy(runtime.Pet.gameObject);
                }
                if (runtime.Dummy != null)
                {
                    GameData.PlayerCombat?.MyMeleeRange?.GetNPCsInRange()
                        ?.Remove(runtime.Dummy);
                    GameData.PlayerControl?.Myself?.NearbyEnemies
                        ?.Remove(runtime.Dummy);
                }
                if (runtime.DummyObject != null)
                    UnityEngine.Object.Destroy(runtime.DummyObject);
                foreach (Item clone in runtime.EquipmentClones)
                    if (clone != null) UnityEngine.Object.Destroy(clone);
                runtime.EquipmentClones.Clear();
                if (runtime.PetWeaponClone != null)
                    UnityEngine.Object.Destroy(runtime.PetWeaponClone);
            }
            if (ReferenceEquals(_activeRuntime, runtime)) _activeRuntime = null;
            CleanupExistingPet();
            if (GameData.PlayerControl != null)
                GameData.PlayerControl.CurrentTarget = null;
        }

        private static void CleanupExistingPet()
        {
            NPC pet = GameData.PlayerControl?.Myself?.MyCharmedNPC;
            if (pet == null) return;
            GameData.PlayerControl.Myself.MyCharmedNPC = null;
            UnityEngine.Object.Destroy(pet.gameObject);
        }

        private static void ClearStatuses(Stats stats)
        {
            if (stats?.StatusEffects == null) return;
            for (int i = stats.StatusEffects.Length - 1; i >= 0; i--)
            {
                StatusEffect status = stats.StatusEffects[i];
                if (status == null) continue;
                status.Effect = null;
                status.Duration = 0f;
                status.fromPlayer = false;
                status.bonusDmg = 0;
                status.CastedByPC = false;
                status.Owner = null;
                status.CreditDPS = null;
            }
        }

        private static void SaveNativeState()
        {
            if (GameData.GM == null) return;
            AllowGuardedSave = true;
            try { GameData.GM.SaveGameData(wScene: false); }
            finally { AllowGuardedSave = false; }
        }

        private static void WriteReport(List<ParseResult> results,
            BenchmarkOptions options, bool complete, string guard)
        {
            var output = new StringBuilder(32768);
            output.AppendLine("Erenshor Classic Skills - Live DPS Balance Benchmark");
            output.AppendLine("UTC: " + DateTime.UtcNow.ToString("O"));
            output.AppendLine("Addon: " + SkillsPlugin.PluginVersion);
            output.AppendLine("Mode: " + options.DisplayName);
            output.AppendLine("Status: " + (complete ? "COMPLETE" : "IN PROGRESS"));
            output.AppendLine("Measurement: native training-dummy HP delta from real in-game combat");
            output.AppendLine("Levels: " + string.Join(", ", options.Levels));
            output.AppendLine("Simulation speeds: " + string.Join(", ",
                (options.TimeScales ?? new[]
                    { options.Quick ? QuickTimeScale : FullTimeScale })
                .Select(value => value.ToString("F0") + "x")));
            output.AppendLine("Gear powers: " + string.Join(", ",
                (options.GearPowers ?? new[] { GearPower.Optimized })
                .Select(value => value.ToString().ToLowerInvariant())));
            output.AppendLine($"Targets: {DummyHealth:N0} HP; native same-level " +
                "armor (level * 15); deterministic midpoint native resistance " +
                "(round(level * 0.85))");
            output.AppendLine($"Profiles: same race base stats (base HP " +
                $"{_standardBaseHp}, base mana {_standardBaseMana}); exact " +
                "native level-based proficiency allowance, offensively optimized; " +
                "strong class-compatible base-quality native pre-raid armor, " +
                "accessories, aura, charm, and weapons; no consumables");
            output.AppendLine("Gear: best scored class-appropriate item in every available slot at the active power ceiling: budget=55% of player level, progression=80%, optimized=100%; real item stats, scaling, worn effects, auras, and weapon/wand/bow procs remain active");
            output.AppendLine("Gear boundary: raid-level gear and unrelated addon gear are excluded; Monk-only handwraps, class armor, charm, and mirrored-budget auras are included for Monk because they are its intended obtainable equipment");
            output.AppendLine("Bone Thralls visibly equip the strongest usable native one-handed weapon under the same gear ceiling with normal tier-capped pet stat projection (exact level 32 only for optimized player level 35)");
            output.AppendLine("Rotation: targeted direct/AE spells, damage-only PBAE spells, damaging DoTs, compatible attack/ranged skills, autoattacks, resource recovery, pre-summoned pets and offensive self buffs; Monk uses Hundred Fists and Five-Point Focus but reserves native Kick for interrupts");
            output.AppendLine("Monk progression: custom technique damage uses the smooth live-audit coefficient anchored at levels 5/10/15/20/25/30/35; Open Hand Discipline gives sub-masterwork handwraps intrinsic level scaling while level-30+ handwraps use their real item damage without catch-up scaling");
            output.AppendLine("Progression boundary: native spells and skills are limited to the verified pre-raid catalog plus explicit non-raid 0.7 additions; all Planar March raid-drop abilities are excluded");
            output.AppendLine("Excluded: percent-current-health and execute effects, encounter-specific targets, and non-damaging crowd control");
            output.AppendLine("Isolation: only John EverQuest and the current controlled pet may damage or apply status effects to the dummy; unrelated NPC, SimPlayer, stale projectile, and environmental damage is rejected");
            output.AppendLine("Attribution: native resolved damage divided into weapon, skill, direct spell, area spell, DoT, pet, and unmatched HP delta");
            output.AppendLine("Scenarios: baseline at every level; maximum class-specific Ascensions only at level 35, where Erenshor begins awarding Ascension XP (full mode)");
            output.AppendLine("Seeds: paired by level, duration, gear power, and repetition across classes and baseline/Ascension scenarios");
            output.AppendLine("Configuration guard: paired repetitions within each gear power must use byte-identical loadouts, combat-boundary stats, active effects, weapons, and pet weapons");
            output.AppendLine($"Cadence telemetry: every parse records rendered simulation frames, wall-clock duration, action opportunities, and successful actions; the benchmark temporarily requests {BenchmarkTargetFrameRate} FPS at 1280x720 with VSync disabled, minimum runtime graphics quality, and suppressed 3D camera drawing, then restores the player's display, quality, and camera settings");
            output.AppendLine($"Cadence validity: accelerated results are rejected unless effective simulation speed is at least {MinimumCadenceScaleRatio:P0} of requested speed and render cadence is at least {MinimumAcceleratedFramesPerSecond:F1} frames per simulated second");
            output.AppendLine($"Cadence retries: a rejected sample is discarded and retried after a five-second real-time cooldown, up to {MaximumCadenceAttempts} attempts; no rejected sample enters the report");
            output.AppendLine($"Input cadence: missed non-casting 0.08-second action slots are replayed in order, up to {MaximumActionReplaysPerFrame} per rendered frame; native casting suppresses queued inputs and all native per-action cooldown/resource checks remain authoritative");
            if (options.MonkPeerGuard)
                output.AppendLine("Balance guard: Monk must reach at least 85% of the median of the three strongest native damage controls, stay below 115% of the strongest native class, rank in the top four overall, and keep paired-repetition spread at or below 20%");
            else if (options.TimeScaleGuard)
                output.AppendLine("Time-scale guard: every compared class's accelerated mean DPS must remain within 10% of its paired real-time mean for the same level, scenario, duration, and gear power; repetition spread is capped at 20% for Monk and 35% for native controls");
            else
                output.AppendLine("Balance guard: Necromancer must reach at least 85% of the median of the three strongest native damage controls, stay below 115% of the strongest native class, and keep paired-repetition spread at or below 35%; all-class rank is also reported");
            output.AppendLine();
            output.AppendLine("level\tclass\tscenario\tgear_power\tgear_level_ceiling\tduration_s\ttime_scale\tsimulation_frames\twall_duration_s\taction_opportunities\tsuccessful_actions\trepetition\tseed\tdamage\tdps\tweapon_damage\tskill_damage\tdirect_spell_damage\tarea_spell_damage\tdot_damage\tpet_damage\tother_damage\tmana_start\tmana_end\thp_start\thp_min\thp_end\talive\tweapon\tloadout\tstats\tactive_effects\tpet\tpet_weapon\tpet_summon_cost\tpet_summon_max_mana\tpet_summon_affordable\tdamage_sources\tspells\tskills");
            foreach (ParseResult value in results)
                output.Append(value.Level).Append('\t')
                    .Append(value.ClassName).Append('\t')
                    .Append(value.Scenario).Append('\t')
                    .Append(value.GearPower).Append('\t')
                    .Append(value.GearLevelCeiling).Append('\t')
                    .Append(value.DurationSeconds).Append('\t')
                    .Append(value.TimeScale.ToString("F0")).Append('\t')
                    .Append(value.SimulationFrames).Append('\t')
                    .Append(value.WallDurationSeconds.ToString("F2")).Append('\t')
                    .Append(value.ActionOpportunities).Append('\t')
                    .Append(value.SuccessfulActions).Append('\t')
                    .Append(value.Repetition).Append('\t')
                    .Append(value.Seed).Append('\t')
                    .Append(value.Damage).Append('\t')
                    .Append(value.Dps.ToString("F2")).Append('\t')
                    .Append(value.WeaponDamage).Append('\t')
                    .Append(value.SkillDamage).Append('\t')
                    .Append(value.DirectSpellDamage).Append('\t')
                    .Append(value.AreaSpellDamage).Append('\t')
                    .Append(value.DotDamage).Append('\t')
                    .Append(value.PetDamage).Append('\t')
                    .Append(value.OtherDamage).Append('\t')
                    .Append(value.StartingMana).Append('\t')
                    .Append(value.EndingMana).Append('\t')
                    .Append(value.StartingHealth).Append('\t')
                    .Append(value.MinimumHealth).Append('\t')
                    .Append(value.EndingHealth).Append('\t')
                    .Append(value.PlayerAlive).Append('\t')
                    .Append(Clean(value.Weapon)).Append('\t')
                    .Append(Clean(value.Loadout)).Append('\t')
                    .Append(Clean(value.Stats)).Append('\t')
                    .Append(Clean(value.ActiveEffects)).Append('\t')
                    .Append(Clean(value.Pet)).Append('\t')
                    .Append(Clean(value.PetWeapon)).Append('\t')
                    .Append(value.PetSummonCost).Append('\t')
                    .Append(value.PetSummonMaximumMana).Append('\t')
                    .Append(value.PetSummonAffordable).Append('\t')
                    .Append(Clean(DamageCounts(value.DamageSources))).Append('\t')
                    .Append(Clean(Counts(value.Spells))).Append('\t')
                    .AppendLine(Clean(Counts(value.Skills)));
            AppendSummaries(output, results);
            if (!string.IsNullOrEmpty(guard))
            {
                output.AppendLine();
                output.AppendLine("BALANCE GUARD");
                output.AppendLine(guard);
            }
            if (!SafeFileIO.TryWriteAllTextAtomic(LastReportPath,
                output.ToString(), out string error))
                throw new IOException("could not write DPS report: " + error);
        }

        private static void AppendSummaries(StringBuilder output,
            List<ParseResult> results)
        {
            foreach (var group in results.GroupBy(value => new
                { value.Level, value.Scenario, value.DurationSeconds,
                  value.TimeScale, value.GearPower,
                  value.GearLevelCeiling })
                .OrderBy(value => value.Key.Level)
                .ThenBy(value => value.Key.GearLevelCeiling)
                .ThenBy(value => value.Key.Scenario)
                .ThenBy(value => value.Key.DurationSeconds)
                .ThenBy(value => value.Key.TimeScale))
            {
                output.AppendLine();
                output.AppendLine($"SUMMARY L{group.Key.Level} " +
                    $"{group.Key.Scenario} {group.Key.GearPower} gear " +
                    $"(ceiling L{group.Key.GearLevelCeiling}) " +
                    $"{group.Key.DurationSeconds}s at " +
                    $"{group.Key.TimeScale:F0}x");
                int rank = 1;
                foreach (var row in group.GroupBy(value => value.ClassName)
                    .Select(values => new
                    {
                        ClassName = values.Key,
                        Dps = values.Average(value => value.Dps),
                        Minimum = values.Min(value => value.Dps),
                        Maximum = values.Max(value => value.Dps),
                        Weapon = values.Average(value =>
                            value.WeaponDamage / (double)value.DurationSeconds),
                        Skill = values.Average(value =>
                            value.SkillDamage / (double)value.DurationSeconds),
                        Direct = values.Average(value =>
                            value.DirectSpellDamage / (double)value.DurationSeconds),
                        Area = values.Average(value =>
                            value.AreaSpellDamage / (double)value.DurationSeconds),
                        Dot = values.Average(value =>
                            value.DotDamage / (double)value.DurationSeconds),
                        Pet = values.Average(value =>
                            value.PetDamage / (double)value.DurationSeconds),
                        Other = values.Average(value =>
                            value.OtherDamage / (double)value.DurationSeconds),
                        Count = values.Count()
                    }).OrderByDescending(value => value.Dps))
                    output.AppendLine(
                        $"{rank++}. {row.ClassName}: {row.Dps:F2} DPS " +
                        $"(range {row.Minimum:F2}-{row.Maximum:F2}, n={row.Count}); " +
                        $"sources weapon {row.Weapon:F2}, skill {row.Skill:F2}, " +
                        $"direct {row.Direct:F2}, area {row.Area:F2}, " +
                        $"DoT {row.Dot:F2}, pet {row.Pet:F2}, other {row.Other:F2}");
            }
        }

        private static string BuildPeerGuard(
            List<ParseResult> results, string targetClassName, bool quick,
            out bool passed)
        {
            var messages = new List<string>();
            passed = true;
            foreach (var group in results.GroupBy(value => new
                { value.Level, value.Scenario, value.DurationSeconds,
                  value.TimeScale, value.GearPower,
                  value.GearLevelCeiling }))
            {
                Dictionary<string, double> means = group
                    .GroupBy(value => value.ClassName)
                    .ToDictionary(values => values.Key,
                        values => values.Average(value => value.Dps));
                int expectedClassCount = string.Equals(targetClassName, "Monk",
                    StringComparison.OrdinalIgnoreCase) ? 8 : 7;
                if (!means.TryGetValue(targetClassName, out double targetDps) ||
                    means.Count < expectedClassCount) continue;
                List<double> targetSamples = group
                    .Where(value => string.Equals(value.ClassName,
                        targetClassName, StringComparison.OrdinalIgnoreCase))
                    .Select(value => value.Dps).ToList();
                double repetitionSpread = targetDps <= 0 ||
                    targetSamples.Count < 2 ? 0 :
                    (targetSamples.Max() - targetSamples.Min()) / targetDps;
                double repetitionLimit = string.Equals(targetClassName, "Monk",
                    StringComparison.OrdinalIgnoreCase)
                    ? MaximumMonkRepetitionSpread
                    : MaximumRepetitionSpread;
                bool repeatable = quick || targetSamples.Count < 2 ||
                    repetitionSpread <= repetitionLimit;
                // Native classes span tank, support, hybrid, and pure damage
                // roles. The upper-three median is a stable damage-role peer
                // reference without treating a low-output support control as
                // the addon class's balance target.
                List<double> native = means.Where(value =>
                        !string.Equals(value.Key, "Necromancer",
                            StringComparison.OrdinalIgnoreCase) &&
                        !string.Equals(value.Key, "Monk",
                            StringComparison.OrdinalIgnoreCase))
                    .Select(value => value.Value)
                    .OrderByDescending(value => value).ToList();
                if (native.Count != 6) continue;
                double strongest = native[0];
                double median = native.Take(3).OrderBy(value => value)
                    .ElementAt(1);
                double upperReference = strongest;
                double lowerRatio = median <= 0 ? 0 : targetDps / median;
                double upperRatio = upperReference <= 0
                    ? 0 : targetDps / upperReference;
                int rank = means.Values.Count(value => value > targetDps) + 1;
                bool rankPass = !string.Equals(targetClassName, "Monk",
                    StringComparison.OrdinalIgnoreCase) || rank <= 4;
                bool rowPassed = quick ||
                    (lowerRatio >= MinimumPeerRatio &&
                     upperRatio <= MaximumPeerRatio && repeatable &&
                     rankPass);
                passed &= rowPassed;
                string verdict = quick
                    ? "VALIDATION ONLY; guard enforced in full mode"
                    : (rowPassed ? "PASS" : "FAIL");
                messages.Add(
                    $"L{group.Key.Level} {group.Key.Scenario} " +
                    $"{group.Key.GearPower} gear (ceiling " +
                    $"L{group.Key.GearLevelCeiling}) " +
                    $"{group.Key.DurationSeconds}s at " +
                    $"{group.Key.TimeScale:F0}x: " +
                    $"{targetClassName} {targetDps:F2} DPS, rank " +
                    $"{rank}/{expectedClassCount}, native " +
                    $"upper-three median {median:F2} (lower ratio {lowerRatio:P0}), " +
                    $"strongest native {upperReference:F2} (upper ratio " +
                    $"{upperRatio:P0}), repetition spread " +
                    $"{repetitionSpread:P0}, {verdict} " +
                    $"(minimum {MinimumPeerRatio:P0}, maximum " +
                    $"{MaximumPeerRatio:P0}, repeatability maximum " +
                    $"{repetitionLimit:P0}" +
                    (string.Equals(targetClassName, "Monk",
                        StringComparison.OrdinalIgnoreCase)
                        ? ", required rank top 4)"
                        : ")"));
            }
            if (messages.Count == 0)
            {
                passed = false;
                return $"FAIL: no complete {ExpectedClassCountFor(
                    targetClassName)}-class comparison groups were recorded";
            }
            return string.Join("; ", messages);
        }

        private static int ExpectedClassCountFor(string targetClassName) =>
            string.Equals(targetClassName, "Monk",
                StringComparison.OrdinalIgnoreCase) ? 8 : 7;

        private static string BuildTimeScaleGuard(
            List<ParseResult> results, string targetClassName,
            out bool passed)
        {
            var messages = new List<string>();
            passed = true;
            IEnumerable<string> targetClasses =
                string.IsNullOrWhiteSpace(targetClassName)
                    ? results.Select(value => value.ClassName)
                        .Distinct(StringComparer.OrdinalIgnoreCase)
                    : new[] { targetClassName };
            foreach (string className in targetClasses
                .OrderBy(value => value))
            foreach (var group in results.Where(value => string.Equals(
                        value.ClassName, className,
                        StringComparison.OrdinalIgnoreCase))
                    .GroupBy(value => new
                {
                    value.Level,
                    value.Scenario,
                    value.DurationSeconds,
                    value.GearPower,
                    value.GearLevelCeiling
                }))
            {
                List<ParseResult> real = group.Where(value =>
                    Mathf.Approximately(value.TimeScale, FullTimeScale))
                    .ToList();
                if (real.Count == 0) continue;
                double realMean = real.Average(value => value.Dps);
                double realSpread = RelativeSpread(real, realMean);
                foreach (var accelerated in group.Where(value =>
                        !Mathf.Approximately(value.TimeScale, FullTimeScale))
                    .GroupBy(value => value.TimeScale))
                {
                    double acceleratedMean =
                        accelerated.Average(value => value.Dps);
                    double acceleratedSpread = RelativeSpread(
                        accelerated.ToList(), acceleratedMean);
                    double ratio = realMean <= 0
                        ? 0 : acceleratedMean / realMean;
                    double bias = Math.Abs(ratio - 1.0);
                    double spreadLimit = string.Equals(className, "Monk",
                        StringComparison.OrdinalIgnoreCase)
                        ? MaximumMonkRepetitionSpread
                        : MaximumRepetitionSpread;
                    bool rowPassed = realMean > 0 &&
                        bias <= MaximumTimeScaleBias &&
                        realSpread <= spreadLimit &&
                        acceleratedSpread <= spreadLimit;
                    passed &= rowPassed;
                    messages.Add(
                        $"{className} L{group.Key.Level} " +
                        $"{group.Key.Scenario} " +
                        $"{group.Key.GearPower} gear (ceiling " +
                        $"L{group.Key.GearLevelCeiling}) " +
                        $"{group.Key.DurationSeconds}s: real-time " +
                        $"{realMean:F2} DPS vs {accelerated.Key:F0}x " +
                        $"{acceleratedMean:F2} DPS (ratio {ratio:P0}, " +
                        $"bias {bias:P1}); repetition spread real " +
                        $"{realSpread:P1}, accelerated " +
                        $"{acceleratedSpread:P1} (maximum " +
                        $"{spreadLimit:P0}); " +
                        (rowPassed ? "PASS" : "FAIL"));
                }
            }
            if (messages.Count == 0)
            {
                passed = false;
                return "FAIL: no paired real-time and accelerated class parses were recorded";
            }
            return string.Join("; ", messages);
        }

        private static double RelativeSpread(
            List<ParseResult> samples, double mean)
        {
            return mean <= 0 || samples.Count < 2 ? 0 :
                (samples.Max(value => value.Dps) -
                 samples.Min(value => value.Dps)) / mean;
        }

        private static string Counts(Dictionary<string, int> values)
        {
            return values.Count == 0 ? "none" : string.Join(", ", values
                .OrderBy(value => value.Key)
                .Select(value => value.Key + "=" + value.Value));
        }

        private static string DamageCounts(Dictionary<string, long> values)
        {
            return values.Count == 0 ? "none" : string.Join(", ", values
                .OrderBy(value => value.Key)
                .Select(value => value.Key + "=" + value.Value));
        }

        private static void AddDamageSource(ParseResult result,
            string category, string source, int damage)
        {
            if (result == null || damage <= 0) return;
            string key = (category ?? "other") + ":" +
                (string.IsNullOrWhiteSpace(source) ? "unresolved" : source);
            result.DamageSources.TryGetValue(key, out long current);
            result.DamageSources[key] = current + damage;
        }

        private static string Clean(string value)
        {
            return (value ?? "").Replace('\t', ' ').Replace('\r', ' ')
                .Replace('\n', ' ');
        }

        private static void Increment(Dictionary<string, int> values,
            string key)
        {
            key = string.IsNullOrWhiteSpace(key) ? "unnamed" : key;
            values.TryGetValue(key, out int count);
            values[key] = count + 1;
        }

        private static void ValidateRuntimePreconditions()
        {
            ValidateWorldStillLoaded();
            string livePlayerName = GameData.PlayerStats?.MyName ?? "";
            if (!string.Equals(livePlayerName,
                    AddonTestMode.NecromancerQaCharacterName,
                    StringComparison.Ordinal))
                throw new InvalidOperationException(
                    "the benchmark is restricted to John EverQuest; live " +
                    $"player is '{livePlayerName}'");
            if (GameData.InCombat || GameData.Autoattacking)
                throw new InvalidOperationException(
                    "leave combat before starting the benchmark");
            if (GameData.PlayerControl.Myself.MyCharmedNPC != null)
                throw new InvalidOperationException(
                    "dismiss the active pet before starting the benchmark");
            if (Necromancer.NecromancerPetEquipment.HasGear)
                throw new InvalidOperationException(
                    "pet escrow must be empty for the standardized parse");
        }

        private static void ValidateWorldStillLoaded()
        {
            if (GameData.PlayerControl == null || GameData.PlayerStats == null ||
                GameData.PlayerInv == null || GameData.PlayerCombat == null ||
                GameData.PlayerControl.Myself == null ||
                GameData.ItemDB?.ItemDB == null ||
                GameData.SpellDatabase?.SpellDatabase == null ||
                GameData.SkillDatabase?.SkillDatabase == null ||
                Necromancer.NecromancerCatalog.NecromancerClass == null)
                throw new InvalidOperationException(
                    "the loaded game world or combat databases became unavailable");
        }
    }

    /// <summary>Never let an autosave serialize a temporary benchmark profile.</summary>
    [HarmonyPatch(typeof(GameManager), nameof(GameManager.SaveGameData))]
    internal static class Patch_Level35DpsGameManagerSaveGuard
    {
        private static bool Prefix()
        {
            if (!Level35DpsBenchmark.IsRunning ||
                Level35DpsBenchmark.AllowGuardedSave) return true;
            SkillsPlugin.Log.LogInfo(
                "Deferred native SaveGameData during the live DPS benchmark.");
            return false;
        }
    }

    /// <summary>Also cover rare callers that invoke the native disk writer directly.</summary>
    [HarmonyPatch(typeof(GameData), nameof(GameData.SaveData))]
    internal static class Patch_Level35DpsGameDataSaveGuard
    {
        private static bool Prefix(ref bool __result)
        {
            if (!Level35DpsBenchmark.IsRunning ||
                Level35DpsBenchmark.AllowGuardedSave) return true;
            __result = false;
            SkillsPlugin.Log.LogInfo(
                "Deferred native SaveData during the live DPS benchmark.");
            return false;
        }
    }

    /// <summary>
    /// Keep unrelated world combat out of a controlled target-dummy parse.
    /// Native self-damage uses SelfDamageMe/SelfDamageMeFlat and remains active.
    /// </summary>
    [HarmonyPatch(typeof(Character), nameof(Character.DamageMe))]
    internal static class Patch_Level35DpsPhysicalIsolation
    {
        private static bool Prefix(Character __instance, Character _attacker,
            ref int __result)
        {
            if (!Level35DpsBenchmark.ShouldSuppressDamage(__instance,
                    _attacker)) return true;
            __result = 0;
            return false;
        }

        private static void Postfix(Character __instance,
            Character _attacker, int __result)
        {
            Level35DpsBenchmark.RecordPhysicalDamage(__instance, _attacker,
                __result);
        }
    }

    [HarmonyPatch(typeof(Character), nameof(Character.BleedDamageMe))]
    internal static class Patch_Level35DpsBleedIsolation
    {
        private static bool Prefix(Character __instance, Character _attacker,
            ref int __result)
        {
            if (!Level35DpsBenchmark.ShouldSuppressDamage(__instance,
                    _attacker)) return true;
            __result = 0;
            return false;
        }

        private static void Postfix(Character __instance,
            Character _attacker, int __result)
        {
            Level35DpsBenchmark.RecordBleedDamage(__instance, _attacker,
                __result);
        }
    }

    [HarmonyPatch(typeof(Character), nameof(Character.MagicDamageMe))]
    internal static class Patch_Level35DpsMagicIsolation
    {
        private static bool Prefix(Character __instance, Character _attacker,
            ref int __result)
        {
            if (!Level35DpsBenchmark.ShouldSuppressDamage(__instance,
                    _attacker)) return true;
            __result = 0;
            return false;
        }

        private static void Postfix(Character __instance,
            Character _attacker, int __result)
        {
            Level35DpsBenchmark.RecordMagicDamage(__instance, _attacker,
                __result);
        }
    }

    [HarmonyPatch(typeof(Character), nameof(Character.EnvironmentalDamageMe))]
    internal static class Patch_Level35DpsEnvironmentIsolation
    {
        private static bool Prefix(Character __instance, ref int __result)
        {
            if (!Level35DpsBenchmark.ShouldSuppressEnvironmentalDamage(
                    __instance)) return true;
            __result = 0;
            return false;
        }

        private static void Postfix(Character __instance, int __result)
        {
            Level35DpsBenchmark.RecordEnvironmentalDamage(__instance,
                __result);
        }
    }

    /// <summary>Keep benchmark AE/PBAE effects from mutating nearby world NPCs.</summary>
    [HarmonyPatch(typeof(Stats), nameof(Stats.AddStatusEffect),
        new[] { typeof(Spell), typeof(bool), typeof(int), typeof(Character) })]
    internal static class Patch_Level35DpsStatusIsolation
    {
        private static bool Prefix(Stats __instance,
            Spell spell, Character _specificCaster, ref int __result)
        {
            if (!Level35DpsBenchmark.ShouldSuppressStatus(__instance,
                    _specificCaster, spell)) return true;
            __result = -1;
            return false;
        }
    }

    /// <summary>Marks native periodic-effect resolution for DoT attribution.</summary>
    [HarmonyPatch(typeof(Stats), "TickEffects")]
    internal static class Patch_Level35DpsStatusTickAttribution
    {
        private static readonly HashSet<int> LoggedFailures =
            new HashSet<int>();

        private static void Prefix(Stats __instance)
        {
            Level35DpsBenchmark.BeginStatusTick(__instance);
        }

        private static Exception Finalizer(Stats __instance,
            Exception __exception)
        {
            Level35DpsBenchmark.EndStatusTick(__instance);
            if (__exception == null) return null;

            // Native Stats.TickEffects can throw on effects whose Owner or
            // MyNPC reference went away (it dereferences Myself.MyNPC without
            // the null guard it uses everywhere else in that method). The
            // exception already aborted the tick, and because TickEffects is
            // called from Stats.Update it also aborts the rest of Update —
            // including regen — for that character every 3 seconds.
            //
            // Rethrowing only adds a Unity-side stack trace that BepInEx
            // writes to disk synchronously, once per affected character per
            // tick. Log the full detail once per Stats instance and swallow
            // the repeats so the remainder of Stats.Update still runs.
            LogFailureOnce(__instance, __exception);
            return null;
        }

        private static void LogFailureOnce(Stats stats, Exception exception)
        {
            int instanceId = stats != null ? stats.GetInstanceID() : 0;
            if (!LoggedFailures.Add(instanceId)) return;
            string objectName = stats != null && stats.transform != null
                ? stats.transform.name : "<missing Stats>";
            string characterName = stats?.Myself != null
                ? stats.Myself.transform.name : "<missing Character>";
            string nullSlots = stats?.StatusEffects == null
                ? "array-null"
                : string.Join(",", Enumerable.Range(0,
                    stats.StatusEffects.Length)
                    .Where(index => stats.StatusEffects[index] == null));
            int activeEffects = stats?.StatusEffects == null ? -1 :
                stats.StatusEffects.Count(status => status?.Effect != null);
            string effects = stats?.StatusEffects == null
                ? "<array-null>"
                : string.Join("|", stats.StatusEffects.Select(
                    (status, index) => new { status, index })
                    .Where(entry => entry.status?.Effect != null)
                    .Select(entry =>
                    {
                        Spell effect = entry.status.Effect;
                        return entry.index + ":" +
                            (effect.Id ?? "<no-id>") + "/" +
                            (effect.SpellName ?? "<no-name>") +
                            ",type=" + effect.Type +
                            ",line=" + effect.Line +
                            ",duration=" + entry.status.Duration +
                            ",damage=" + effect.TargetDamage +
                            ",healing=" + effect.TargetHealing +
                            ",mana=" + effect.Mana +
                            ",unstable=" + effect.UnstableDuration +
                            ",reap=" + effect.ReapAndRenew +
                            ",owner=" +
                            (entry.status.Owner != null
                                ? entry.status.Owner.transform.name
                                : "<none>") +
                            ",credit=" +
                            (entry.status.CreditDPS != null
                                ? entry.status.CreditDPS.transform.name
                                : "<none>");
                    }));
            string nullIcons = stats?.StatusIcons == null
                ? "array-null"
                : string.Join(",", Enumerable.Range(0,
                    stats.StatusIcons.Count).Where(index =>
                        stats.StatusIcons[index] == null));
            string currentTarget = GameData.PlayerControl?.CurrentTarget != null
                ? GameData.PlayerControl.CurrentTarget.transform.name : "<none>";
            SkillsPlugin.Log.LogError(
                "Stats.TickEffects failure diagnostic: object=" + objectName +
                "; character=" + characterName + "; instance=" + instanceId +
                "; nullSlots=" + (string.IsNullOrEmpty(nullSlots)
                    ? "none" : nullSlots) + "; activeEffects=" + activeEffects +
                "; effects=" + effects +
                "; statusIcons=" + (stats?.StatusIcons?.Count ?? -1) +
                "; nullIcons=" + (string.IsNullOrEmpty(nullIcons)
                    ? "none" : nullIcons) +
                "; inventoryNull=" + (stats?.MyInv == null) +
                "; npcNull=" + (stats?.Myself?.MyNPC == null) +
                "; class=" +
                    (stats?.CharacterClass?.ClassName ?? "<none>") +
                "; currentTarget=" + currentTarget + "; exception=" +
                exception.GetType().Name + ": " + exception.Message +
                // The finalizer swallows the exception, so this one-shot
                // diagnostic is the only place the trace survives.
                "\n" + exception.StackTrace);
        }
    }

    /// <summary>Marks native spell resolution for direct versus area attribution.</summary>
    [HarmonyPatch(typeof(SpellVessel), "ResolveSpell")]
    internal static class Patch_Level35DpsSpellResolveAttribution
    {
        private static void Prefix(SpellVessel __instance)
        {
            Level35DpsBenchmark.BeginSpellResolve(__instance?.spell);
        }

        private static Exception Finalizer(Exception __exception)
        {
            Level35DpsBenchmark.EndSpellResolve();
            return __exception;
        }
    }

    /// <summary>
    /// Wand autoattacks deliver magic damage outside SpellVessel.ResolveSpell.
    /// Mark that synchronous endpoint so it is reported as weapon damage rather
    /// than as an unresolved direct spell.
    /// </summary>
    [HarmonyPatch(typeof(WandBolt), "DeliverDamage")]
    internal static class Patch_Level35DpsWandBoltAttribution
    {
        private static void Prefix()
        {
            Level35DpsBenchmark.BeginWandBolt();
        }

        private static Exception Finalizer(Exception __exception)
        {
            Level35DpsBenchmark.EndWandBolt();
            return __exception;
        }
    }
}
