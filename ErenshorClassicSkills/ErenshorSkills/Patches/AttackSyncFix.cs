using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using BepInEx.Configuration;
using ErenshorSkills.Monk;
using HarmonyLib;
using UnityEngine;

namespace ErenshorSkills.Patches
{
    // ═══════════════════════════════════════════════════════════════════
    // ATTACK SYNC FIX (merged) + COMBAT WEAPON SKILL XP
    // Originally a separate mod (AttackSyncFix v1.3.0).
    // Merged here so both use the same Harmony instance — no conflicts.
    // ═══════════════════════════════════════════════════════════════════

    /// <summary>Config entries for attack sync timing.</summary>
    public static class AttackSyncConfig
    {
        public static ConfigEntry<float> PlayerHitFraction;
        public static ConfigEntry<float> NPCHitFraction;
        public static ConfigEntry<float> MinHitDelay;
        public static ConfigEntry<float> MaxHitDelay;
        public static ConfigEntry<bool> SyncPlayerAttacks;
        public static ConfigEntry<bool> SyncNPCAttacks;
        public static ConfigEntry<bool> SyncSimPlayerAttacks;
        public static ConfigEntry<bool> DebugLogging;

        public static void Init(BepInEx.Configuration.ConfigFile cfg)
        {
            PlayerHitFraction = cfg.Bind("AttackSync", "PlayerHitFraction", 0.45f,
                new ConfigDescription("Fraction of swing animation when hit lands.",
                    new AcceptableValueRange<float>(0.1f, 0.9f)));
            NPCHitFraction = cfg.Bind("AttackSync", "NPCHitFraction", 0.40f,
                new ConfigDescription("Fraction of NPC swing when hit sound plays.",
                    new AcceptableValueRange<float>(0.1f, 0.9f)));
            MinHitDelay = cfg.Bind("AttackSync", "MinHitDelay", 0.08f,
                new ConfigDescription("Minimum hit delay in seconds.",
                    new AcceptableValueRange<float>(0.02f, 0.3f)));
            MaxHitDelay = cfg.Bind("AttackSync", "MaxHitDelay", 0.8f,
                new ConfigDescription("Maximum hit delay in seconds.",
                    new AcceptableValueRange<float>(0.2f, 2.0f)));
            SyncPlayerAttacks = cfg.Bind("AttackSync", "SyncPlayerAttacks", true,
                "Enable sync fix for player melee auto-attacks.");
            SyncNPCAttacks = cfg.Bind("AttackSync", "SyncNPCAttacks", true,
                "Enable sync fix for NPC/enemy melee attacks.");
            SyncSimPlayerAttacks = cfg.Bind("AttackSync", "SyncSimPlayerAttacks", true,
                "Enable sync fix for SimPlayer (party member) melee attacks.");
            DebugLogging = cfg.Bind("AttackSync", "DebugLogging", false,
                "Log animation lengths and computed delays.");
        }
    }

    /// <summary>Animation/timing helpers.</summary>
    public static class AttackSyncHelpers
    {
        public static float GetSwingAnimLength(Animator anim)
        {
            if (anim == null) return 0.7f;
            AnimatorClipInfo[] clips = anim.GetNextAnimatorClipInfo(0);
            if (clips != null && clips.Length > 0)
            {
                float len = clips[0].clip.length;
                if (len > 0.05f && len < 1.5f) return len;
            }
            clips = anim.GetCurrentAnimatorClipInfo(0);
            if (clips != null && clips.Length > 0)
            {
                float len = clips[0].clip.length;
                if (len > 0.05f && len < 1.5f) return len;
            }
            return 0.7f;
        }

        public static float CalcHitDelay(float animLength, float fraction, float animSpeedMult)
        {
            float effective = animLength / Mathf.Max(animSpeedMult, 0.1f);
            float delay = effective * fraction;
            return Mathf.Clamp(delay, AttackSyncConfig.MinHitDelay.Value, AttackSyncConfig.MaxHitDelay.Value);
        }

        public static float GetAnimSpeedMultiplier(Stats stats)
        {
            float currentDelay = SyncReflection.GetCurrentMHAtkDelay(stats);
            return (currentDelay < 80f) ? 1.25f : 1.0f;
        }
    }

    // ══════════════════════════════════════════════════════════════
    // PLAYER MELEE — Prefix replaces PerformAttacks, awards weapon XP
    // ══════════════════════════════════════════════════════════════

    [HarmonyPatch(typeof(PlayerCombat), "PerformAttacks")]
    public static class Patch_PerformAttacks
    {
        [ThreadStatic] internal static bool HandwrapAnimationEligible;
        [ThreadStatic] internal static bool HandwrapAnimationHandled;

        [HarmonyPrefix, HarmonyPriority(Priority.Last)]
        public static bool Prefix(PlayerCombat __instance, Character target, int attackCount, bool isMainHand)
        {
            HandwrapAnimationEligible = false;
            HandwrapAnimationHandled = false;

            // --- Attack Sync: skip if disabled or ranged ---
            var myStats = SyncReflection.GetPlayerStats(__instance);
            var myAnim = SyncReflection.GetPlayerAnim(__instance);
            var myControl = SyncReflection.GetPlayerControl(__instance);
            if (myStats == null || myAnim == null || myControl == null) return true;

            bool isWand = SyncReflection.CallCheckForWand(__instance, isMainHand);
            bool isBow = SyncReflection.CallCheckForBow(__instance, isMainHand);

            // Wands and bows use their own code path — let vanilla handle them
            if (isWand || isBow) return true;

            bool useHandwrapAnimation =
                MonkRuntime.UsesHandwrapAutoAnimation(isMainHand);
            // Handwraps always need the replacement path so Erenshor never
            // layers its invisible one-handed weapon swing over the authored
            // punches. Attack synchronization remains optional for every
            // other weapon.
            if (!AttackSyncConfig.SyncPlayerAttacks.Value &&
                !useHandwrapAnimation)
                return true;
            if (target == null || !target.Alive) return true;

            bool inRange = SyncReflection.CallCheckForRange(__instance, isWand, isBow, target);
            // Preserve vanilla's HandleInvalidTarget feedback and delay reset.
            if (!inRange) return true;
            HandwrapAnimationEligible = useHandwrapAnimation;
            if (useHandwrapAnimation)
            {
                MonkRuntime.PlayHandwrapAutoAnimation(isMainHand);
                HandwrapAnimationHandled = true;
            }

            for (int i = 0; i <= attackCount; i++)
            {
                target.FlagForFactionHit(true);

                // Invisible Monk handwraps use the authored punch driver.
                // Do not start Erenshor's generic one-handed animation first:
                // even a same-frame trigger reset can leave its double-attack
                // state layered under the punch.
                if (!useHandwrapAnimation)
                {
                    if (i == 0)
                        myAnim.SetTrigger(isMainHand
                            ? "MeleeSwing" : "DualWield");
                    if (i == 1 && isMainHand)
                        myAnim.SetBool("DoubleAttack", true);
                    if (i == 1 && !isMainHand)
                        myAnim.SetBool("OHDoubleAttack", true);
                    if (i == 2) myAnim.SetTrigger("MeleeSwing");
                }

                int dmg = myStats.CalcMeleeDamage(
                    isMainHand ? myStats.MyInv.MHDmg : myStats.MyInv.OHDmg,
                    target.MyStats.Level, target.MyStats, 0);

                string avoidance = SyncReflection.CallCheckTargetInnateAvoidance(__instance, target);

                // July 2026 added Opportunist to the vanilla melee path. Preserve it
                // when this prefix replaces PlayerCombat.PerformAttacks.
                if (string.IsNullOrEmpty(avoidance) &&
                    SyncReflection.TryTriggerOpportunist(__instance, myStats, target))
                    continue;

                // Start two-phase coroutine for synced hit
                var hitData = new DelayedPlayerHitData
                {
                    Instance = __instance, Target = target, Dmg = dmg,
                    IsMainHand = isMainHand, MyStats = myStats, MyControl = myControl,
                    Avoidance = avoidance, Anim = myAnim,
                    ProcSlot = isMainHand ? GameData.PlayerInv.MH : GameData.PlayerInv.OH,
                    ApplyInitialStanceDamage = i == 0 && isMainHand
                };
                SkillsPlugin.Instance.StartCoroutine(TwoPhasePlayerHit(hitData));
            }

            // Reset attack delay through reflection. Erenshor build 24247002
            // changed the CLR signature from Reset*AtkDelay() to
            // Reset*AtkDelay(bool, float), despite retaining optional C# args.
            // A direct call compiled against either version fails on the other.
            SyncReflection.ResetAttackDelay(myStats, isMainHand);

            return false; // Skip original
        }

        internal static void ResetHandwrapAnimationContext()
        {
            HandwrapAnimationEligible = false;
            HandwrapAnimationHandled = false;
        }

        private static IEnumerator TwoPhasePlayerHit(DelayedPlayerHitData data)
        {
            yield return null; // Wait one frame for animator transition

            if (data.Target == null || !data.Target.Alive || data.MyStats == null)
                yield break;

            float animLength = AttackSyncHelpers.GetSwingAnimLength(data.Anim);
            float animSpeed = AttackSyncHelpers.GetAnimSpeedMultiplier(data.MyStats);
            float fraction = AttackSyncConfig.PlayerHitFraction.Value;
            float hitDelay = AttackSyncHelpers.CalcHitDelay(animLength, fraction, animSpeed);

            if (AttackSyncConfig.DebugLogging.Value)
                SkillsPlugin.Log.LogInfo(
                    $"[Player] animLen={animLength:F3}s speed={animSpeed:F2}x " +
                    $"fraction={fraction:F2} => delay={hitDelay:F3}s");

            float remaining = hitDelay - Time.deltaTime;
            if (remaining > 0f)
                yield return new WaitForSeconds(remaining);

            if (data.Target == null || !data.Target.Alive || data.MyStats == null)
                yield break;

            // Swing audio occurs at impact time for both hits and misses.
            if (data.MyStats.Myself?.MyAttackSound != null)
                data.MyStats.Myself.MyAudio.PlayOneShot(data.MyStats.Myself.MyAttackSound,
                    data.MyStats.Myself.MyAudio.volume * GameData.SFXVol * GameData.MasterVol);

            if (data.Avoidance != "")
            {
                UpdateSocialLog.CombatLogAdd(new ChatLogLine(
                    "You try to hit " + data.Target.name + ", but " + data.Target.name + " " + data.Avoidance,
                    ChatLogLine.LogType.PlayerHits));
                yield break;
            }

            try
            {
                var stance = data.MyStats.CombatStance;
                if (data.ApplyInitialStanceDamage && stance != null &&
                    stance.SelfDamagePerAttack > 0f)
                {
                    float pct = stance.SelfDamagePerAttack / 100f;
                    data.MyStats.Myself.SelfDamageMeFlat(
                        Mathf.RoundToInt(data.MyStats.CurrentMaxHP * pct));
                }

                data.MyStats.CheckProc(data.ProcSlot, data.Target);
                SyncReflection.CallHandleDamageResult(data.Instance, data.Target,
                    ref data.Dmg, data.IsMainHand);
                if (data.Dmg > 0) data.MyStats.RecentDmg = 240f;
                if (stance != null && stance.SelfDamagePerAttack > 0f)
                    data.MyStats.Myself.SelfDamageMe(stance.SelfDamagePerAttack);
            }
            catch (Exception ex)
            {
                SkillsPlugin.Log.LogError($"Delayed player hit failed: {ex}");
            }
        }
    }

    public class DelayedPlayerHitData
    {
        public PlayerCombat Instance; public Character Target; public int Dmg;
        public bool IsMainHand; public Stats MyStats; public PlayerControl MyControl;
        public string Avoidance; public Animator Anim;
        public ItemIcon ProcSlot; public bool ApplyInitialStanceDamage;
    }

    /// <summary>
    /// Weapon proficiency is independent of the optional animation sync. A
    /// postfix observes every vanilla or replaced melee attempt, so disabling
    /// SyncPlayerAttacks no longer disables melee skill progression.
    /// </summary>
    [HarmonyPatch(typeof(PlayerCombat), "PerformAttacks")]
    public static class Patch_MeleeXP
    {
        public struct AttackState
        {
            public bool XpEligible;
            public bool PreviousContextActive;
            public bool PreviousMainHand;
        }

        [ThreadStatic] internal static bool ContextActive;
        [ThreadStatic] internal static bool CurrentIsMainHand;

        [HarmonyPrefix, HarmonyPriority(Priority.First)]
        public static void Prefix(PlayerCombat __instance, Character target,
            bool isMainHand, out AttackState __state)
        {
            __state = new AttackState
            {
                PreviousContextActive = ContextActive,
                PreviousMainHand = CurrentIsMainHand
            };
            ContextActive = true;
            CurrentIsMainHand = isMainHand;
            if (!SkillsSaveManager.HasLoaded ||
                !SkillsPlugin.CfgEnableCombatSkills.Value ||
                target == null || !target.Alive) return;
            try
            {
                var slot = isMainHand ? GameData.PlayerInv?.MH : GameData.PlayerInv?.OH;
                var weapon = slot?.MyItem;
                if (weapon != null && (weapon.IsBow || weapon.IsWand)) return;
                __state.XpEligible = SyncReflection.CallCheckForRange(
                    __instance, false, false, target);
            }
            catch { __state.XpEligible = false; }
        }

        public static void Postfix(Character target, int attackCount,
            bool isMainHand, AttackState __state)
        {
            try
            {
                if (!__state.XpEligible) return;
                var slot = isMainHand ? GameData.PlayerInv?.MH : GameData.PlayerInv?.OH;
                var weapon = slot?.MyItem;
                if (weapon != null && (weapon.IsBow || weapon.IsWand)) return;
                int attempts = Mathf.Max(1, attackCount + 1);
                for (int i = 0; i < attempts; i++)
                {
                    if (weapon == null || weapon == GameData.PlayerInv?.Empty ||
                        string.IsNullOrEmpty(weapon.ItemName) || weapon.ItemName == "Empty")
                    {
                        var skill = SkillsSaveManager.Data.HandToHand;
                        skill.TimesUsed++;
                        skill.Successes++;
                        SkillXpEngine.AwardXp(skill, 2f, showChat: false);
                    }
                    else
                    {
                        Skills.CombatSkills.OnWeaponHit(weapon,
                            isMainHand && (GameData.PlayerInv?.TwoHandPrimary ?? false), 0f);
                    }
                }
            }
            catch (Exception ex)
            {
                SkillsPlugin.Log.LogWarning($"Melee skill XP failed: {ex.Message}");
            }
            finally
            {
                RestoreContext(__state);
            }
        }

        public static Exception Finalizer(Exception __exception, AttackState __state)
        {
            RestoreContext(__state);
            return __exception;
        }

        private static void RestoreContext(AttackState state)
        {
            ContextActive = state.PreviousContextActive;
            CurrentIsMainHand = state.PreviousMainHand;
        }
    }

    // ══════════════════════════════════════════════════════════════
    // WAND XP — Postfix on DoWandAttack (vanilla handles wand logic)
    // ══════════════════════════════════════════════════════════════

    [HarmonyPatch(typeof(PlayerCombat), "DoWandAttack")]
    public static class Patch_WandXP
    {
        public static void Postfix(PlayerCombat __instance, Character _target)
        {
            if (!SkillsSaveManager.HasLoaded ||
                !SkillsPlugin.CfgEnableCombatSkills.Value || _target == null) return;
            try
            {
                var skill = SkillsSaveManager.Data.Wands;
                skill.TimesUsed++; skill.Successes++;
                SkillXpEngine.AwardXp(skill, 2.2f, showChat: false);
            }
            catch { }
        }
    }

    // ══════════════════════════════════════════════════════════════
    // ARCHERY XP — observe the exact vanilla projectile path. This fires only
    // after range/facing validation and once per arrow actually launched.
    // ══════════════════════════════════════════════════════════════

    [HarmonyPatch(typeof(PlayerCombat), "DoBowAttack",
        new Type[] { typeof(Character), typeof(int) })]
    public static class Patch_ArcheryXP
    {
        public static void Postfix(Character _target)
        {
            if (!SkillsSaveManager.HasLoaded ||
                !SkillsPlugin.CfgEnableCombatSkills.Value) return;
            try
            {
                if (_target == null) return;
                var mhItem = GameData.PlayerInv?.MH?.MyItem;
                if (mhItem == null || !mhItem.IsBow) return;

                var skill = SkillsSaveManager.Data.Archery;
                skill.TimesUsed++; skill.Successes++;
                SkillXpEngine.AwardXp(skill, 2.5f, showChat: false);
            }
            catch { }
        }
    }

    // ══════════════════════════════════════════════════════════════
    // NPC MELEE SOUND SYNC
    // ══════════════════════════════════════════════════════════════

    [HarmonyPatch(typeof(NPC), "PerformMeleeHit")]
    public static class Patch_NPCMeleeSync
    {
        public struct SoundState
        {
            public Character Character;
            public AudioClip Clip;
        }

        [HarmonyPrefix, HarmonyPriority(Priority.First)]
        public static void Prefix(NPC __instance, int baseDamage, bool isOffhand,
            out SoundState __state)
        {
            __state = new SoundState();
            bool isSim = __instance.SimPlayer;
            if (isSim && !AttackSyncConfig.SyncSimPlayerAttacks.Value) return;
            if (!isSim && !AttackSyncConfig.SyncNPCAttacks.Value) return;
            if (SyncReflection.GetNPCMHBow(__instance) || SyncReflection.GetNPCMHWand(__instance)) return;

            Character myself = __instance.GetChar();
            if (myself == null || __instance.CurrentAggroTarget == null) return;
            if (myself.MyAttackSound != null)
            {
                __state.Character = myself;
                __state.Clip = myself.MyAttackSound;
                myself.MyAttackSound = null;
            }

            float fraction = isSim
                ? AttackSyncConfig.PlayerHitFraction.Value
                : AttackSyncConfig.NPCHitFraction.Value;
            Animator anim = myself.GetComponent<Animator>();
            float animSpeed = 1.0f;
            if (myself.MyStats != null)
            {
                float currentDelay = SyncReflection.GetCurrentMHAtkDelay(myself.MyStats);
                if (currentDelay < 80f) animSpeed = 1.25f;
            }

            SkillsPlugin.Instance.StartCoroutine(TwoPhaseNPCSound(__instance, anim, fraction, animSpeed));
        }

        [HarmonyPostfix, HarmonyPriority(Priority.Last)]
        public static void Postfix(SoundState __state) => RestoreSound(__state);

        [HarmonyFinalizer]
        public static Exception Finalizer(Exception __exception, SoundState __state)
        {
            RestoreSound(__state);
            return __exception;
        }

        private static void RestoreSound(SoundState state)
        {
            if (state.Character != null && state.Clip != null)
                state.Character.MyAttackSound = state.Clip;
        }

        private static IEnumerator TwoPhaseNPCSound(NPC npc, Animator anim, float fraction, float animSpeed)
        {
            yield return null;
            if (npc == null) yield break;
            Character myself = npc.GetChar();
            if (myself == null || myself.MyAttackSound == null) yield break;

            float animLength = AttackSyncHelpers.GetSwingAnimLength(anim);
            float hitDelay = AttackSyncHelpers.CalcHitDelay(animLength, fraction, animSpeed);

            if (AttackSyncConfig.DebugLogging.Value)
                SkillsPlugin.Log.LogInfo(
                    $"[NPC:{myself.name}] animLen={animLength:F3}s speed={animSpeed:F2}x " +
                    $"fraction={fraction:F2} => delay={hitDelay:F3}s");

            float remaining = hitDelay - Time.deltaTime;
            if (remaining > 0f) yield return new WaitForSeconds(remaining);

            if (npc == null) yield break;
            myself = npc.GetChar();
            if (myself == null || myself.MyAttackSound == null) yield break;

            if (GameData.PlayerControl == null) yield break;
            float dist = Vector3.Distance(myself.transform.position, GameData.PlayerControl.transform.position);
            bool isTargetPlayer = npc.CurrentAggroTarget == GameData.PlayerControl.Myself;
            if (dist < 8f || isTargetPlayer)
                myself.MyAudio.PlayOneShot(myself.MyAttackSound,
                    myself.MyAudio.volume * GameData.CombatVol * GameData.MasterVol);
        }
    }

    // ══════════════════════════════════════════════════════════════
    // REFLECTION HELPERS (from AttackSyncFix)
    // ══════════════════════════════════════════════════════════════

    public static class SyncReflection
    {
        private static FieldInfo _pcMyStats, _pcMyAnim, _pcMyControl;
        private static MethodInfo _pcCheckForWand, _pcCheckForBow, _pcCheckForRange;
        private static MethodInfo _pcCheckTargetInnateAvoidance, _pcHandleDamageResult;
        private static MethodInfo _pcCheckIfBehind;
        private static MethodInfo _statsResetMHAtkDelay, _statsResetOHAtkDelay;
        private static FieldInfo _miscOpportunist, _miscBackstab;
        private static FieldInfo _statsCurrentMHAtkDelay;
        private static FieldInfo _npcMHBow, _npcMHWand;
        private static bool _npcFieldsResolved;

        static SyncReflection()
        {
            var pcType = typeof(PlayerCombat);
            var flags = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;
            _pcMyStats = pcType.GetField("myStats", flags);
            _pcMyAnim = pcType.GetField("MyAnim", flags);
            _pcMyControl = pcType.GetField("MyControl", flags);
            _pcCheckForWand = pcType.GetMethod("CheckForWand", flags);
            _pcCheckForBow = pcType.GetMethod("CheckForBow", flags);
            _pcCheckForRange = pcType.GetMethod("CheckForRange", flags);
            _pcCheckTargetInnateAvoidance = pcType.GetMethod("CheckTargetInnateAvoidance", flags);
            _pcHandleDamageResult = pcType.GetMethod("HandleDamageResult", flags);
            _pcCheckIfBehind = pcType.GetMethod("CheckIfBehind", flags);
            _statsCurrentMHAtkDelay = typeof(Stats).GetField("CurrentMHAtkDelay", flags);
            _statsResetMHAtkDelay = FindDelayReset("ResetMHAtkDelay", flags);
            _statsResetOHAtkDelay = FindDelayReset("ResetOHAtkDelay", flags);
            _miscOpportunist = typeof(Misc).GetField("Opportunist", flags);
            _miscBackstab = typeof(Misc).GetField("Backstab", flags);
        }

        public static Stats GetPlayerStats(PlayerCombat pc) => _pcMyStats?.GetValue(pc) as Stats;
        public static Animator GetPlayerAnim(PlayerCombat pc)
        {
            // Pre-Planar-March builds cached a private PlayerCombat.MyAnim field.
            var anim = _pcMyAnim?.GetValue(pc) as Animator;
            if (anim != null) return anim;

            // Build 24247002 removed that field and routes animation through the
            // Character component instead.
            try
            {
                var character = pc?.GetComponent<Character>();
                anim = character?.GetMyAnim();
                if (anim != null) return anim;
                return pc?.GetComponent<Animator>();
            }
            catch { return null; }
        }
        public static PlayerControl GetPlayerControl(PlayerCombat pc) => _pcMyControl?.GetValue(pc) as PlayerControl;

        private static MethodInfo FindDelayReset(string name, BindingFlags flags)
        {
            MethodInfo fallback = null;
            foreach (var method in typeof(Stats).GetMethods(flags))
            {
                if (method.Name != name) continue;
                int count = method.GetParameters().Length;
                if (count == 0) return method;
                if (count == 2) fallback = method;
                else if (fallback == null) fallback = method;
            }
            return fallback;
        }

        public static void ResetAttackDelay(Stats stats, bool mainHand)
        {
            if (stats == null) return;
            var method = mainHand ? _statsResetMHAtkDelay : _statsResetOHAtkDelay;
            if (method == null) return;

            try
            {
                int count = method.GetParameters().Length;
                if (count == 0)
                    method.Invoke(stats, null);
                else if (count == 2)
                    method.Invoke(stats, new object[] { false, 0f });
                else
                    method.Invoke(stats, new object[] { 0f });
            }
            catch (Exception ex)
            {
                SkillsPlugin.Log.LogWarning($"Attack delay reset failed: {ex.Message}");
            }
        }

        public static bool TryTriggerOpportunist(PlayerCombat pc, Stats stats, Character target)
        {
            try
            {
                if (_miscOpportunist == null || _miscBackstab == null ||
                    _pcCheckIfBehind == null ||
                    stats?.Myself?.MySkills == null || target == null)
                    return false;

                var opportunist = _miscOpportunist.GetValue(GameData.Misc) as Skill;
                var backstab = _miscBackstab.GetValue(GameData.Misc) as Skill;
                if (opportunist == null || backstab == null ||
                    !stats.Myself.MySkills.KnownSkills.Contains(opportunist))
                    return false;
                if (UnityEngine.Random.Range(0, 100) >= 2)
                    return false;
                if (!((bool)(_pcCheckIfBehind.Invoke(pc, new object[] { target }) ?? false)))
                    return false;

                stats.Myself.MySkills.DoSkill(backstab, target);
                return true;
            }
            catch { return false; }
        }

        public static float GetCurrentMHAtkDelay(Stats stats)
        {
            if (_statsCurrentMHAtkDelay != null) return (float)_statsCurrentMHAtkDelay.GetValue(stats);
            return stats.BaseMHAtkDelay + stats.MyInv.MHDelay * 60f;
        }

        public static bool CallCheckForWand(PlayerCombat pc, bool mh)
            => (bool)(_pcCheckForWand?.Invoke(pc, new object[] { mh }) ?? false);
        public static bool CallCheckForBow(PlayerCombat pc, bool mh)
            => (bool)(_pcCheckForBow?.Invoke(pc, new object[] { mh }) ?? false);
        public static bool CallCheckForRange(PlayerCombat pc, bool w, bool b, Character t)
            => (bool)(_pcCheckForRange?.Invoke(pc, new object[] { w, b, t }) ?? false);
        public static string CallCheckTargetInnateAvoidance(PlayerCombat pc, Character t)
            => (string)(_pcCheckTargetInnateAvoidance?.Invoke(pc, new object[] { t }) ?? "");

        public static void CallHandleDamageResult(PlayerCombat pc, Character target, ref int dmg, bool isMainHand)
        {
            if (_pcHandleDamageResult != null)
            {
                object[] args = new object[] { target, dmg, isMainHand };
                _pcHandleDamageResult.Invoke(pc, args);
                dmg = (int)args[1];
            }
        }

        private static void ResolveNPCFields()
        {
            if (_npcFieldsResolved) return;
            var flags = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;
            _npcMHBow = typeof(NPC).GetField("MHBow", flags);
            _npcMHWand = typeof(NPC).GetField("MHWand", flags);
            _npcFieldsResolved = true;
        }

        public static bool GetNPCMHBow(NPC npc) { ResolveNPCFields(); return (bool)(_npcMHBow?.GetValue(npc) ?? false); }
        public static bool GetNPCMHWand(NPC npc) { ResolveNPCFields(); return (bool)(_npcMHWand?.GetValue(npc) ?? false); }
    }
}
