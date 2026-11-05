using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using UnityEngine;

namespace ErenshorSkills.Monk
{
    internal static class MonkRuntime
    {
        internal sealed class SkillUseState
        {
            internal MonkTechniqueDefinition Definition;
            internal Transform Caster;
            internal Transform Target;
            internal Character CasterCharacter;
            internal Character TargetCharacter;
            internal bool DamageContext;
        }

        private static readonly FieldInfo ActualMainDelay =
            AccessTools.Field(typeof(Stats), "ActualMHAtkDelay");
        private static readonly FieldInfo ActualOffDelay =
            AccessTools.Field(typeof(Stats), "ActualOHAtkDelay");

        private static float _feignUntil;
        private static float _stoneUntil;
        private static float _flurryUntil;
        private static float _counterUntil;
        private static float _reedUntil;
        private static float _emptyUntil;
        private static float _focusUntil;
        private static float _centeredUntil;
        private static float _centeredNextTick;
        private static int _centeredTicks;
        private static int _centeredHealPerTick;
        private static float _harmonyUntil;
        private static float _harmonyNextTick;
        private static int _harmonyTicks;
        private static int _harmonyHealPerTick;
        private static int _techniqueDamageDepth;
        private static string _activeTechniqueKey;
        private static int _counterDamageDepth;

        internal static int LastJabAutoDamage { get; private set; }
        internal static int LastJabBonusDamage { get; private set; }
        internal static int LastJabCompositeDamage { get; private set; }

        internal static bool IsFeigning =>
            MonkCatalog.IsMonkPlayer() && Time.time < _feignUntil;
        internal static bool StoneStanceActive =>
            MonkCatalog.IsMonkPlayer() && Time.time < _stoneUntil;
        internal static bool HundredFistsActive =>
            MonkCatalog.IsMonkPlayer() && Time.time < _flurryUntil;
        internal static bool CounteringPalmActive =>
            MonkCatalog.IsMonkPlayer() && Time.time < _counterUntil;
        internal static bool ReedInTheWindActive =>
            MonkCatalog.IsMonkPlayer() && Time.time < _reedUntil;
        internal static bool EmptyBodyActive =>
            MonkCatalog.IsMonkPlayer() && Time.time < _emptyUntil;
        internal static bool FivePointFocusActive =>
            MonkCatalog.IsMonkPlayer() && Time.time < _focusUntil;
        internal static bool TechniqueDamageActive =>
            _techniqueDamageDepth > 0 || _counterDamageDepth > 0;

        internal static bool PrepareSkillUse(UseSkill user, Skill skill,
            Character target, ref bool result, out SkillUseState state)
        {
            state = null;
            if (!MonkCatalog.TryGetDefinition(skill,
                    out MonkTechniqueDefinition definition))
                return true;
            Stats stats = user?.GetComponent<Stats>();
            Character actor = user != null ? user.GetComponent<Character>() : null;
            if (SkillsPlugin.CfgEnableMonk?.Value != true || stats == null ||
                !MonkCatalog.IsMonkClass(stats.CharacterClass))
            {
                if (actor != null && !actor.isNPC) ChatHelper.SendCombat(
                    "Only a Monk can use that technique.", "#E7C76F");
                result = false;
                return false;
            }
            float reach = definition.Kind == MonkTechniqueKind.StoneThrow ? 18f : 5f;
            bool npcTargetMatches = actor?.MyNPC != null &&
                actor.MyNPC.CurrentAggroTarget == target;
            if (!MonkCombatPolicy.CanActivate(actor != null && actor.Alive,
                actor != null && actor.isNPC, definition.IsOffensive,
                target != null && target.Alive, target == actor,
                npcTargetMatches, target != null && actor != null
                    ? (target.transform.position - actor.transform.position).sqrMagnitude
                    : float.PositiveInfinity, reach))
            {
                result = false;
                return false;
            }
            if (stats.Level < definition.Level)
            {
                ChatHelper.SendCombat(
                    $"{definition.Name} requires Monk level {definition.Level}.",
                    "#E7C76F");
                result = false;
                return false;
            }
            if (definition.Kind == MonkTechniqueKind.Passive)
            {
                ChatHelper.SendCombat(
                    "Open Hand Discipline is always active while using handwraps.",
                    "#9FD6C7");
                result = false;
                return false;
            }
            if (!definition.IsOffensive)
            {
                result = ActivateUtility(user, definition);
                // Utility techniques complete entirely inside this prefix and
                // therefore never receive a SkillUseState in the postfix.
                if (result && IsRestorativeTechnique(definition.Kind) &&
                    actor == GameData.PlayerControl?.Myself)
                    AwardAlterationUse(definition);
                return false;
            }
            if (definition.Kind == MonkTechniqueKind.StoneThrow)
            {
                result = ActivateStoneThrow(user, target, definition);
                return false;
            }

            state = new SkillUseState
            {
                Definition = definition,
                Caster = user.transform,
                Target = target?.transform,
                CasterCharacter = user.GetComponent<Character>(),
                TargetCharacter = target,
                DamageContext = true
            };
            // Names the technique whose native DoSkill call is now on the
            // stack, so the damage patch can special-case it. Cleared in
            // CompleteSkillUse.
            _activeTechniqueKey = definition.Key;
            if (string.Equals(definition.Key, "jab",
                    StringComparison.Ordinal))
            {
                LastJabAutoDamage = 0;
                LastJabBonusDamage = 0;
                LastJabCompositeDamage = 0;
            }
            _techniqueDamageDepth++;
            return true;
        }

        internal static bool PrepareSpellUse(CastSpell caster, Spell spell,
            Stats requestedTarget, ref bool result)
        {
            if (!MonkCatalog.TryGetDefinition(spell,
                    out MonkTechniqueDefinition definition))
                return true;
            Character character = caster?.MyChar;
            if (character == null || SkillsPlugin.CfgEnableMonk?.Value != true ||
                !MonkCatalog.IsMonkClass(character.MyStats?.CharacterClass))
            {
                result = false;
                return false;
            }
            UseSkill skillUser = character?.MySkills;
            Skill backingSkill = MonkCatalog.GetSkill(definition.Key);
            if (character == null || skillUser == null ||
                backingSkill == null)
            {
                result = false;
                ChatHelper.SendCombat(
                    "That martial technique is not ready.", "#E7C76F");
                return false;
            }

            Stats stats = character.MyStats;
            int manaCost = Mathf.Max(0, spell.ManaCost);
            if (stats == null || stats.GetCurrentMana() < manaCost)
            {
                result = false;
                if (!character.isNPC)
                    ChatHelper.SendCombat(
                        "You need more mana to use " +
                        definition.Name + ".", "#80D8FF");
                return false;
            }

            Character target = definition.IsOffensive
                ? requestedTarget?.Myself ??
                  (character.isNPC ? character.MyNPC?.CurrentAggroTarget
                    : GameData.PlayerControl?.CurrentTarget)
                : character;
            result = character.isNPC
                ? skillUser.DoSkillNoChecks(backingSkill, target)
                : skillUser.DoSkill(backingSkill, target);
            if (SkillsPlugin.CfgEnableTestMode?.Value == true)
                SkillsPlugin.Log?.LogInfo(
                    $"[Monk Technique] {definition.Key} result={result}");
            if (result)
            {
                stats.ReduceMana(manaCost);
                if (character == GameData.PlayerControl?.Myself)
                    ApplySpellCooldown(spell);
                // Impact VFX is anchored here, on the same call that spends
                // the mana, rather than deep inside the animation driver whose
                // impact-release step proved unreliable in play. The driver is
                // told not to double-fire.
                if (definition.IsAttack && SkillsPlugin.Instance != null)
                    SkillsPlugin.Instance.StartCoroutine(
                        PlayImpactVfxAfterDelay(definition,
                            character.transform, target?.transform));
            }
            return false;
        }

        private static IEnumerator PlayImpactVfxAfterDelay(
            MonkTechniqueDefinition definition, Transform caster,
            Transform target)
        {
            yield return new WaitForSeconds(Mathf.Max(0.05f,
                MonkAnimationDriver.GetEffectImpactTime(definition)));
            if (caster != null) MonkVfx.Play(definition, caster, target);
        }

        private static void ApplySpellCooldown(Spell spell)
        {
            if (spell == null || GameData.GM?.HKManager?.AllHotkeys == null)
                return;
            float cooldown = Mathf.Max(0f, spell.Cooldown * 60f);
            foreach (Hotkeys hotkey in GameData.GM.HKManager.AllHotkeys)
                if (hotkey != null &&
                    ReferenceEquals(hotkey.AssignedSpell, spell))
                    hotkey.Cooldown = Mathf.Max(hotkey.Cooldown, cooldown);
        }

        internal static void CompleteSkillUse(SkillUseState state,
            bool succeeded)
        {
            if (state == null) return;
            if (state.DamageContext)
            {
                _techniqueDamageDepth = Mathf.Max(0,
                    _techniqueDamageDepth - 1);
                state.DamageContext = false;
                _activeTechniqueKey = null;
            }
            if (!succeeded || state.Definition == null) return;
            // One award per successful offensive technique, mirroring the one
            // award per weapon swing the native melee path gives. Utility
            // techniques carry Power 0 and train nothing.
            if (state.Definition.Power > 0 &&
                state.CasterCharacter == GameData.PlayerControl?.Myself)
                Skills.CombatSkills.OnTechniqueHit(
                    Skills.CombatSkills.WeaponType.HandToHand,
                    state.Definition.Power *
                        (state.CasterCharacter?.MyStats?.Level ?? 1));
            // Animation only — the impact VFX is scheduled by PrepareSpellUse
            // on the proven execution path.
            MonkAnimationDriver.Play(state.Definition, state.Caster,
                state.Target, playVfx: false);
            if (state.Definition.FollowUpHits > 0 &&
                state.CasterCharacter != null &&
                state.TargetCharacter != null && SkillsPlugin.Instance != null)
                SkillsPlugin.Instance.StartCoroutine(FollowUpStrikes(state));
        }

        private static bool ActivateUtility(UseSkill user,
            MonkTechniqueDefinition definition)
        {
            Stats stats = user?.GetComponent<Stats>();
            Character character = user?.GetComponent<Character>();
            if (stats == null || character == null || !character.Alive)
                return false;
            if ((stats.Stunned || stats.Feared) && definition.Kind !=
                    MonkTechniqueKind.FocusedWill)
            {
                ChatHelper.SendCombat(
                    "You cannot focus a Monk technique while incapacitated.",
                    "#E7C76F");
                return false;
            }
            if (stats.Invisible) stats.BreakEffectsOnAction();
            Animator animator = user.GetComponent<Animator>();

            switch (definition.Kind)
            {
                case MonkTechniqueKind.FocusedWill:
                {
                    bool wasControlled = stats.Stunned || stats.Feared;
                    int removed = RemoveCrowdControl(stats);
                    stats.Stunned = false;
                    stats.Feared = false;
                    try { stats.CalcStats(); } catch { }
                    if (!wasControlled && removed == 0)
                    {
                        ChatHelper.SendCombat(
                            "Focused Will finds no fear or stun to break.",
                            "#E7C76F");
                        return false;
                    }
                    animator?.SetTrigger("SkillBuff");
                    MonkVfx.Play(definition, user.transform);
                    ChatHelper.SendCombat(
                        "Focused Will breaks the control over you.",
                        "#F0D06B");
                    return true;
                }
                case MonkTechniqueKind.Mend:
                {
                    if (stats.CurrentHP >= stats.CurrentMaxHP)
                    {
                        // Still breathe: refusing silently read as "Mend has
                        // no VFX" whenever it was tested at full health. No
                        // mana or cooldown is spent (result stays false), but
                        // the technique visibly happens.
                        animator?.SetTrigger("SkillBuff");
                        MonkVfx.PlayMendHeal(definition, user.transform);
                        ChatHelper.SendCombat(
                            "You are already at full health.", "#9FD6C7");
                        return false;
                    }
                    float ratio = 0.25f + 0.05f *
                        MonkAscensions.GetRank(user,
                            MonkAscensions.DeepMendId);
                    int before = stats.CurrentHP;
                    stats.HealMe(ScaleHeal(
                        Mathf.RoundToInt(stats.CurrentMaxHP * ratio)));
                    int restored = Mathf.Max(0, stats.CurrentHP - before);
                    animator?.SetTrigger("SkillBuff");
                    MonkVfx.PlayMendHeal(definition, user.transform);
                    ChatHelper.SendCombat(
                        $"Mend restores {restored} health.", "#78D6A8");
                    return true;
                }
                case MonkTechniqueKind.CenteredBreath:
                {
                    if (stats.CurrentHP >= stats.CurrentMaxHP)
                    {
                        ChatHelper.SendCombat(
                            "You are already at full health.", "#9FD6C7");
                        return false;
                    }
                    const float duration = 9f;
                    if (!AddTimedStatus(stats, character,
                            MonkCatalog.GetEffect("centered_breath"), duration))
                        return false;
                    BeginRegeneration(stats, 0.12f, 3, duration,
                        ref _centeredUntil, ref _centeredNextTick,
                        ref _centeredTicks, ref _centeredHealPerTick);
                    animator?.SetTrigger("SkillBuff");
                    MonkVfx.Play(definition, user.transform);
                    ChatHelper.SendCombat(
                        "Your breathing settles into a restorative rhythm.",
                        "#78D6A8");
                    return true;
                }
                case MonkTechniqueKind.FeignDeath:
                {
                    float duration = 12f + 3f *
                        MonkAscensions.GetRank(user,
                            MonkAscensions.PerfectStillnessId);
                    if (!AddTimedStatus(stats, character,
                            MonkCatalog.GetEffect("feign_death"), duration))
                        return false;
                    _feignUntil = Time.time + duration;
                    try { GameData.PlayerCombat?.ForceAttackOff(); } catch { }
                    Necromancer.NecromancerRuntime.ClearPlayerAggro();
                    animator?.SetBool("Dead", true);
                    MonkVfx.Play(definition, user.transform);
                    ChatHelper.SendCombat(
                        $"You feign death for up to {duration:0} seconds.",
                        "#CDBA86");
                    return true;
                }
                case MonkTechniqueKind.CounteringPalm:
                {
                    const float duration = 8f;
                    if (!AddTimedStatus(stats, character,
                            MonkCatalog.GetEffect("countering_palm"), duration))
                        return false;
                    _counterUntil = Time.time + duration;
                    animator?.SetTrigger("SkillBuff");
                    MonkVfx.Play(definition, user.transform);
                    ChatHelper.SendCombat(
                        "You wait behind a poised countering palm.",
                        "#78D6B8");
                    return true;
                }
                case MonkTechniqueKind.BreathOfHarmony:
                {
                    if (stats.CurrentHP >= stats.CurrentMaxHP)
                    {
                        ChatHelper.SendCombat(
                            "You are already at full health.", "#9FD6C7");
                        return false;
                    }
                    const float duration = 15f;
                    if (!AddTimedStatus(stats, character,
                            MonkCatalog.GetEffect("breath_of_harmony"),
                            duration)) return false;
                    BeginRegeneration(stats, 0.20f, 5, duration,
                        ref _harmonyUntil, ref _harmonyNextTick,
                        ref _harmonyTicks, ref _harmonyHealPerTick);
                    animator?.SetTrigger("SkillBuff");
                    MonkVfx.Play(definition, user.transform);
                    ChatHelper.SendCombat(
                        "Breath of Harmony begins restoring your body.",
                        "#78D6A8");
                    return true;
                }
                case MonkTechniqueKind.SilentStep:
                {
                    const float duration = 12f;
                    if (!AddTimedStatus(stats, character,
                            MonkCatalog.GetEffect("silent_step"), duration))
                        return false;
                    animator?.SetTrigger("SkillBuff");
                    MonkVfx.Play(definition, user.transform);
                    ChatHelper.SendCombat(
                        "Your footsteps fade into silence.", "#79BFB4");
                    return true;
                }
                case MonkTechniqueKind.ReedInTheWind:
                {
                    const float duration = 10f;
                    if (!AddTimedStatus(stats, character,
                            MonkCatalog.GetEffect("reed_in_the_wind"),
                            duration)) return false;
                    _reedUntil = Time.time + duration;
                    animator?.SetTrigger("SkillBuff");
                    MonkVfx.Play(definition, user.transform);
                    ChatHelper.SendCombat(
                        "You yield like a reed without breaking.",
                        "#78D698");
                    return true;
                }
                case MonkTechniqueKind.StoneStance:
                {
                    const float duration = 12f;
                    if (!AddTimedStatus(stats, character,
                            MonkCatalog.GetEffect("stone_stance"), duration))
                        return false;
                    _stoneUntil = Time.time + duration;
                    animator?.SetTrigger("SkillBuff");
                    MonkVfx.Play(definition, user.transform);
                    ChatHelper.SendCombat(
                        "You settle into Stone Stance.", "#D5C595");
                    return true;
                }
                case MonkTechniqueKind.EmptyBody:
                {
                    const float duration = 10f;
                    if (!AddTimedStatus(stats, character,
                            MonkCatalog.GetEffect("empty_body"), duration))
                        return false;
                    _emptyUntil = Time.time + duration;
                    animator?.SetTrigger("SkillBuff");
                    MonkVfx.Play(definition, user.transform);
                    ChatHelper.SendCombat(
                        "Your body becomes an empty vessel for pain.",
                        "#A5DDE3");
                    return true;
                }
                case MonkTechniqueKind.PurifyBody:
                {
                    int removed = Purify(stats);
                    animator?.SetTrigger("SkillBuff");
                    MonkVfx.Play(definition, user.transform);
                    ChatHelper.SendCombat(
                        removed == 0
                            ? "Purify Body finds no detrimental effects."
                            : $"Purify Body removes {removed} detrimental effect(s).",
                        "#EAD878");
                    return true;
                }
                case MonkTechniqueKind.HundredFists:
                {
                    float duration = 20f + 3f *
                        MonkAscensions.GetRank(user,
                            MonkAscensions.LingeringFlurryId);
                    if (!AddTimedStatus(stats, character,
                            MonkCatalog.GetEffect("hundred_fists"), duration))
                        return false;
                    _flurryUntil = Time.time + duration;
                    stats.ResetMHAtkDelay();
                    stats.ResetOHAtkDelay();
                    ApplyFlurryDelay(stats, true);
                    ApplyFlurryDelay(stats, false);
                    animator?.SetTrigger("SkillBuff");
                    MonkVfx.Play(definition, user.transform);
                    ChatHelper.SendCombat(
                        $"Hundred Fists quickens your attacks for {duration:0} seconds.",
                        "#F0B74F");
                    return true;
                }
                case MonkTechniqueKind.FivePointFocus:
                {
                    const float duration = 12f;
                    if (!AddTimedStatus(stats, character,
                            MonkCatalog.GetEffect("five_point_focus"),
                            duration)) return false;
                    _focusUntil = Time.time + duration;
                    animator?.SetTrigger("SkillBuff");
                    MonkVfx.Play(definition, user.transform);
                    ChatHelper.SendCombat(
                        "Five points align into a single intent.",
                        "#F0C85A");
                    return true;
                }
                default:
                    return false;
            }
        }

        private static bool ActivateStoneThrow(UseSkill user,
            Character target, MonkTechniqueDefinition definition)
        {
            Stats stats = user?.GetComponent<Stats>();
            Character caster = user?.GetComponent<Character>();
            if (stats == null || caster == null || !caster.Alive) return false;
            if (stats.Stunned || stats.Feared)
            {
                ChatHelper.SendCombat(
                    $"You cannot use {definition.Name} while incapacitated.",
                    "#E7C76F");
                return false;
            }
            if (target == null || !target.Alive || target == caster)
            {
                ChatHelper.SendCombat(
                    $"{definition.Name} requires a living enemy target.",
                    "#E7C76F");
                return false;
            }
            float distance = Vector3.Distance(caster.transform.position,
                target.transform.position);
            if (distance > 18f)
            {
                ChatHelper.SendCombat(
                    $"That target is beyond {definition.Name} range.",
                    "#E7C76F");
                return false;
            }
            if (stats.Invisible) stats.BreakEffectsOnAction();
            try { GameData.PlayerCombat?.ForceAttackOn(); } catch { }
            MonkAnimationDriver.Play(definition, caster.transform,
                target.transform);
            if (SkillsPlugin.Instance != null)
                SkillsPlugin.Instance.StartCoroutine(
                    ResolveResonantPalm(caster, target, definition));
            else
                ResolveResonantPalmDamage(caster, target, definition);
            return true;
        }

        private static IEnumerator ResolveResonantPalm(Character caster,
            Character target, MonkTechniqueDefinition definition)
        {
            yield return new WaitForSeconds(Mathf.Max(0.12f,
                MonkAnimationDriver.GetEffectImpactTime(definition)));
            ResolveResonantPalmDamage(caster, target, definition);
        }

        private static void ResolveResonantPalmDamage(Character caster,
            Character target, MonkTechniqueDefinition definition)
        {
            if (caster == null || target == null || definition == null ||
                !caster.Alive || !target.Alive)
                return;
            Stats stats = caster.MyStats;
            if (stats == null) return;
            int rawDamage = Mathf.Max(1, Mathf.RoundToInt(
                UnityEngine.Random.Range(5f, 20f) / 20f *
                definition.Power * stats.Level));
            int dealt;
            _techniqueDamageDepth++;
            try
            {
                dealt = target.DamageMe(rawDamage, true,
                    GameData.DamageType.Physical, caster, true, false);
            }
            finally
            {
                _techniqueDamageDepth = Mathf.Max(0,
                    _techniqueDamageDepth - 1);
            }
            if (target.MyNPC != null && !target.MyNPC.SimPlayer)
                target.MyNPC.ManageAggro(Mathf.Max(rawDamage * 2,
                    stats.Level * 3), caster);
            if (dealt > 0)
                UpdateSocialLog.CombatLogAdd(new ChatLogLine(
                    $"You use {definition.Name} on {target.transform.name} for " +
                    $"{dealt} physical damage.",
                    ChatLogLine.LogType.PlayerHits));
            else
                UpdateSocialLog.CombatLogAdd(new ChatLogLine(
                    $"{definition.Name} fails to harm " +
                    $"{target.transform.name}.",
                    ChatLogLine.LogType.PlayerHits));
        }

        private static IEnumerator FollowUpStrikes(SkillUseState state)
        {
            yield return new WaitForSeconds(Mathf.Max(0.12f,
                MonkAnimationDriver.GetImpactTime(state.Definition) + 0.08f));
            for (int index = 0; index < state.Definition.FollowUpHits;
                index++)
            {
                if (index > 0) yield return new WaitForSeconds(0.15f);
                Character caster = state.CasterCharacter;
                Character target = state.TargetCharacter;
                if (caster == null || target == null || caster.MyStats == null)
                    yield break;
                bool localPlayer = caster == GameData.PlayerControl?.Myself;
                Character currentTarget = localPlayer
                    ? GameData.PlayerControl?.CurrentTarget
                    : caster.MyNPC?.CurrentAggroTarget;
                if (!MonkCombatPolicy.CanContinueFollowUp(
                    SkillsPlugin.CfgEnableMonk?.Value == true,
                    MonkCatalog.IsMonkClass(caster.MyStats.CharacterClass),
                    caster.Alive, target.Alive, currentTarget == target,
                    (target.transform.position - caster.transform.position).sqrMagnitude))
                    yield break;
                int rawDamage = Mathf.Max(1, Mathf.RoundToInt(
                    caster.MyStats.Level * state.Definition.Power * 0.20f));
                int dealt;
                _techniqueDamageDepth++;
                try
                {
                    dealt = target.DamageMe(rawDamage, true,
                        GameData.DamageType.Physical, caster, true, false);
                }
                finally
                {
                    _techniqueDamageDepth = Mathf.Max(0,
                        _techniqueDamageDepth - 1);
                }
                if (dealt > 0)
                {
                    MonkAudio.PlayImpact(state.Definition,
                        target.transform.position + Vector3.up);
                    string actorName = caster.MyNPC != null
                        ? caster.MyNPC.NPCName : caster.transform.name;
                    if (string.IsNullOrWhiteSpace(actorName))
                        actorName = caster.transform.name;
                    string source = localPlayer ? "You use" : actorName + " uses";
                    UpdateSocialLog.CombatLogAdd(new ChatLogLine(
                        $"{source} {state.Definition.Name} on " +
                        $"{target.transform.name} for {dealt} physical damage (follow-up).",
                        localPlayer ? ChatLogLine.LogType.PlayerHits
                            : ChatLogLine.LogType.OthersHits));
                }
            }
        }

        private static void BeginRegeneration(Stats stats, float ratio,
            int ticks, float duration, ref float until, ref float nextTick,
            ref int remainingTicks, ref int healPerTick)
        {
            int total = Mathf.Max(ticks,
                Mathf.RoundToInt(stats.CurrentMaxHP * ratio));
            healPerTick = Mathf.Max(1, Mathf.CeilToInt(total / (float)ticks));
            remainingTicks = ticks;
            until = Time.time + duration;
            nextTick = Time.time + duration / ticks;
        }

        private static int RemoveCrowdControl(Stats stats)
        {
            int removed = 0;
            if (stats?.StatusEffects == null) return removed;
            for (int index = stats.StatusEffects.Length - 1;
                index >= 0; index--)
            {
                Spell effect = stats.StatusEffects[index]?.Effect;
                if (effect == null || (!effect.StunTarget &&
                    !effect.FearTarget)) continue;
                stats.RemoveStatusEffect(index);
                removed++;
            }
            return removed;
        }

        private static bool AddTimedStatus(Stats stats, Character owner,
            Spell effect, float durationSeconds)
        {
            if (stats == null || owner == null || effect == null) return false;
            RemoveEffect(stats, effect);
            int slot = stats.AddStatusEffect(effect, true, 0, owner);
            if (slot < 0 || slot >= stats.StatusEffects.Length)
            {
                ChatHelper.SendCombat(
                    "No active-effect slot is available for that technique.",
                    "#E7C76F");
                return false;
            }
            stats.StatusEffects[slot].Duration =
                Mathf.Max(1f, durationSeconds / 3f);
            return true;
        }

        private static int Purify(Stats stats)
        {
            int removed = 0;
            if (stats?.StatusEffects == null) return removed;
            for (int index = stats.StatusEffects.Length - 1; index >= 0;
                index--)
            {
                Spell effect = stats.StatusEffects[index]?.Effect;
                if (!IsDetrimental(effect)) continue;
                stats.RemoveStatusEffect(index);
                removed++;
            }
            return removed;
        }

        private static bool IsDetrimental(Spell effect)
        {
            if (effect == null || effect.WornEffect ||
                effect.Line == MonkAuras.AuraLine)
                return false;
            if (effect.TargetDamage > 0 || effect.RootTarget ||
                effect.StunTarget || effect.FearTarget ||
                effect.MovementSpeed < 0f || effect.Haste < 0f ||
                effect.HP < 0 || effect.AC < 0 || effect.Mana < 0 ||
                effect.Str < 0 || effect.Dex < 0 || effect.End < 0 ||
                effect.Agi < 0 || effect.Int < 0 || effect.Wis < 0 ||
                effect.Cha < 0 || effect.MR < 0 || effect.ER < 0 ||
                effect.PR < 0 || effect.VR < 0 ||
                effect.AtkRollModifier < 0)
                return true;
            switch (effect.Line)
            {
                case Spell.SpellLine.Dru_Poison_DOT:
                case Spell.SpellLine.Dru_Void_DOT:
                case Spell.SpellLine.Dru_Magic_DOT:
                case Spell.SpellLine.Dru_Element_DOT:
                case Spell.SpellLine.Dru_Other_Debuff:
                case Spell.SpellLine.Arc_Poison_DOT:
                case Spell.SpellLine.Arc_Void_DOT:
                case Spell.SpellLine.Arc_Magic_DOT:
                case Spell.SpellLine.Arc_Element_DOT:
                case Spell.SpellLine.Arc_Other_Debuff:
                case Spell.SpellLine.Global_Magic_Debuff:
                case Spell.SpellLine.Global_Other_Debuff:
                case Spell.SpellLine.Global_Poison_DOT:
                case Spell.SpellLine.Global_Magic_DOT:
                case Spell.SpellLine.Global_Element_DOT:
                case Spell.SpellLine.Global_Void_DOT:
                case Spell.SpellLine.Sleep:
                case Spell.SpellLine.Stun:
                case Spell.SpellLine.Bleed:
                case Spell.SpellLine.Global_Root:
                case Spell.SpellLine.Attack_Speed_Debuff:
                case Spell.SpellLine.Stormcaller_Move_Debuff:
                case Spell.SpellLine.Braxonian_DOT:
                case Spell.SpellLine.Reav_Fear:
                case Spell.SpellLine.Reav_VoidDOT:
                    return true;
                default:
                    return false;
            }
        }

        internal static void Tick()
        {
            HandToHandReadyIdle.Tick();
            MonkHandwrapVisuals.Tick();
            if (_feignUntil > 0f && (Time.time >= _feignUntil ||
                !HasPlayerEffect("feign_death")))
                EndFeign(false);
            if (_stoneUntil > 0f && (Time.time >= _stoneUntil ||
                !HasPlayerEffect("stone_stance")))
                _stoneUntil = 0f;
            if (_flurryUntil > 0f && (Time.time >= _flurryUntil ||
                !HasPlayerEffect("hundred_fists")))
                _flurryUntil = 0f;
            if (_counterUntil > 0f && (Time.time >= _counterUntil ||
                !HasPlayerEffect("countering_palm")))
                _counterUntil = 0f;
            if (_reedUntil > 0f && (Time.time >= _reedUntil ||
                !HasPlayerEffect("reed_in_the_wind")))
                _reedUntil = 0f;
            if (_emptyUntil > 0f && (Time.time >= _emptyUntil ||
                !HasPlayerEffect("empty_body")))
                _emptyUntil = 0f;
            if (_focusUntil > 0f && (Time.time >= _focusUntil ||
                !HasPlayerEffect("five_point_focus")))
                _focusUntil = 0f;
            TickRegeneration("centered_breath", ref _centeredUntil,
                ref _centeredNextTick, ref _centeredTicks,
                ref _centeredHealPerTick);
            TickRegeneration("breath_of_harmony", ref _harmonyUntil,
                ref _harmonyNextTick, ref _harmonyTicks,
                ref _harmonyHealPerTick);
        }

        /// <summary>
        /// Monk restoration is martial rather than arcane, but it is still
        /// healing and scales with Alteration like any other heal. Both monk
        /// heal sites use the flat Stats.HealMe overload so they can post their
        /// own combat lines without the native one duplicating them, and
        /// Patch_AlterationHeal only covers the five-argument overload — so the
        /// bonus is applied here instead.
        /// </summary>
        private static int ScaleHeal(int amount)
        {
            if (amount <= 0) return amount;
            return Mathf.Max(1, Mathf.RoundToInt(
                amount * Skills.MagicSkills.GetHealMultiplier()));
        }

        private static bool IsRestorativeTechnique(MonkTechniqueKind kind)
        {
            return kind == MonkTechniqueKind.Mend ||
                kind == MonkTechniqueKind.CenteredBreath ||
                kind == MonkTechniqueKind.BreathOfHarmony;
        }

        /// <summary>
        /// Trains Alteration off restorative techniques. Monk techniques run
        /// through UseSkill.DoSkill rather than CastSpell.StartSpell, so the
        /// generic magic-XP postfixes never observe them — without this a Monk
        /// could never raise Alteration and the heal bonus above would be a
        /// permanently dead stat. Awarded once per activation, not per
        /// regeneration tick, so a single Breath cannot farm the skill.
        /// </summary>
        private static void AwardAlterationUse(
            MonkTechniqueDefinition definition)
        {
            if (definition == null) return;
            if (!SkillsSaveManager.HasLoaded) return;
            if (!SkillsPlugin.CfgEnableMagicSkills.Value) return;
            SkillEntry skill = Skills.MagicSkills.GetSkillEntry(
                Skills.MagicSkills.MagicSchool.Alteration);
            if (skill == null) return;
            float xp = 8f + definition.Level * 0.6f;
            SkillXpEngine.SkillCheck(skill, 1f,
                xpOnSuccess: xp, xpOnFailure: xp * 0.5f);
        }

        private static void TickRegeneration(string effectKey,
            ref float until, ref float nextTick, ref int remainingTicks,
            ref int healPerTick)
        {
            if (until <= 0f) return;
            Stats stats = GameData.PlayerStats;
            Character player = GameData.PlayerControl?.Myself;
            if (stats == null || player == null || !player.Alive ||
                Time.time > until + 0.15f || !HasPlayerEffect(effectKey))
            {
                until = 0f;
                remainingTicks = 0;
                return;
            }
            while (remainingTicks > 0 && Time.time >= nextTick)
            {
                int before = stats.CurrentHP;
                // Scaled per tick rather than once at application so a level
                // gained mid-regeneration takes effect on the ticks that
                // follow it.
                stats.HealMe(ScaleHeal(healPerTick));
                int restored = Mathf.Max(0, stats.CurrentHP - before);
                if (restored > 0)
                    UpdateSocialLog.CombatLogAdd(new ChatLogLine(
                        $"{MonkCatalog.GetEffect(effectKey)?.SpellName} " +
                        $"restores {restored} health.",
                        ChatLogLine.LogType.PlayerSpellResults));
                remainingTicks--;
                nextTick += 3f;
            }
            if (remainingTicks <= 0) until = 0f;
        }

        private static bool HasPlayerEffect(string key)
        {
            Stats stats = GameData.PlayerStats;
            Spell effect = MonkCatalog.GetEffect(key);
            return stats?.StatusEffects != null && effect != null &&
                stats.StatusEffects.Any(slot =>
                    ReferenceEquals(slot?.Effect, effect));
        }

        internal static bool BlocksAggro(Character target)
        {
            return target != null &&
                target == GameData.PlayerControl?.Myself && IsFeigning;
        }

        internal static void EndFeign(bool brokenByDamage)
        {
            if (_feignUntil <= 0f) return;
            _feignUntil = 0f;
            Character player = GameData.PlayerControl?.Myself;
            if (player?.Alive == true)
            {
                Animator animator = player.GetComponent<Animator>();
                animator?.SetBool("Dead", false);
                animator?.SetTrigger("Revive");
                RemoveEffect(player.MyStats,
                    MonkCatalog.GetEffect("feign_death"));
            }
            if (brokenByDamage)
                ChatHelper.SendCombat(
                    "Damage breaks your Feign Death.", "#E7C76F");
        }

        private static void RemoveEffect(Stats stats, Spell effect)
        {
            if (stats?.StatusEffects == null || effect == null) return;
            for (int index = stats.StatusEffects.Length - 1;
                index >= 0; index--)
                if (ReferenceEquals(stats.StatusEffects[index]?.Effect,
                        effect))
                    stats.RemoveStatusEffect(index);
        }

        internal static int ModifyTechniqueDamage(int damage,
            Character attacker, Character target = null)
        {
            if (_techniqueDamageDepth <= 0 || damage <= 0 ||
                attacker != GameData.PlayerControl?.Myself ||
                !MonkCatalog.IsMonkPlayer())
                return damage;

            // Jab contains one genuine auto-attack roll plus a separately
            // scaled technique bonus. Keeping the two components explicit
            // guarantees that Jab cannot collapse back to ordinary hand-to-
            // hand damage while still letting equipment, STR and proficiency
            // determine its auto-attack portion.
            if (string.Equals(_activeTechniqueKey, "jab",
                    StringComparison.Ordinal))
            {
                int auto = RollAutoAttackDamage(attacker, target);
                if (auto > 0)
                {
                    float jabMultiplier = 1f + 0.15f *
                        MonkAscensions.GetPlayerRank(
                            MonkAscensions.WayOfIronId);
                    if (FivePointFocusActive) jabMultiplier *= 1.15f;
                    float techniqueScale = GetTechniqueBalanceMultiplier(
                        attacker.MyStats?.Level ?? 1);
                    techniqueScale *= Skills.CombatSkills.GetDamageMultiplier(
                        Skills.CombatSkills.WeaponType.HandToHand);
                    int bonus = Mathf.Max(3, Mathf.RoundToInt(
                        damage * techniqueScale));
                    int composite = Mathf.Max(auto + 1,
                        Mathf.RoundToInt((auto + bonus) * jabMultiplier));
                    LastJabAutoDamage = auto;
                    LastJabBonusDamage = bonus;
                    LastJabCompositeDamage = composite;
                    return composite;
                }
            }

            float multiplier = GetTechniqueBalanceMultiplier(
                attacker.MyStats?.Level ?? 1);
            multiplier *= 1f + 0.15f *
                MonkAscensions.GetPlayerRank(MonkAscensions.WayOfIronId);
            if (FivePointFocusActive) multiplier *= 1.15f;
            // Techniques are unarmed martial arts and should pay out the same
            // Hand to Hand proficiency a handwrap swing does. They resolve
            // their own damage rather than going through Stats.CalcMeleeDamage,
            // so the normal combat-skill postfix never sees them.
            multiplier *= Skills.CombatSkills.GetDamageMultiplier(
                Skills.CombatSkills.WeaponType.HandToHand);
            return Mathf.Max(1, Mathf.RoundToInt(damage * multiplier));
        }

        /// <summary>
        /// One authentic main-hand auto-attack roll against the current
        /// victim. MHDmg is zero for bare hands, which is precisely what the
        /// native auto swing uses then too.
        /// </summary>
        private static int RollAutoAttackDamage(Character attacker,
            Character target)
        {
            try
            {
                Stats stats = attacker?.MyStats;
                if (stats == null) return 0;
                Stats victim = target?.MyStats;
                int targetLevel = victim?.Level ?? stats.Level;
                return stats.CalcMeleeDamage(stats.MyInv?.MHDmg ?? 0,
                    targetLevel, victim, 0);
            }
            catch
            {
                return 0;
            }
        }

        /// <summary>
        /// Live-benchmark-derived Monk damage curve. Native martial SkillPower
        /// grows far more slowly than Erenshor's spell and weapon progression,
        /// so custom techniques need a class coefficient to remain a damage
        /// peer. Interpolation keeps every level smooth while the anchors match
        /// the audited 5-level progression brackets.
        /// </summary>
        internal static float GetTechniqueBalanceMultiplier(int level)
        {
            level = Mathf.Clamp(level, 1, 35);
            if (level <= 5)
                return Mathf.Lerp(6f, 8.5f, (level - 1f) / 4f);
            if (level <= 10)
                return Mathf.Lerp(8.5f, 13f, (level - 5f) / 5f);
            if (level <= 15)
                return Mathf.Lerp(13f, 15f, (level - 10f) / 5f);
            if (level <= 20)
                return Mathf.Lerp(15f, 25f, (level - 15f) / 5f);
            if (level <= 25)
                return Mathf.Lerp(25f, 45f, (level - 20f) / 5f);
            if (level <= 30)
                return Mathf.Lerp(45f, 43f, (level - 25f) / 5f);
            return Mathf.Lerp(43f, 39f, (level - 30f) / 5f);
        }

        internal static int ModifyIncomingDamage(int damage,
            Character target, GameData.DamageType damageType,
            Character attacker)
        {
            if (damage <= 0 || target != GameData.PlayerControl?.Myself)
                return damage;
            if (IsFeigning) EndFeign(true);
            TryCounteringPalm(attacker, damageType);
            float remaining = damage;
            if (StoneStanceActive)
            {
                float reduction = 0.25f + 0.03f *
                    MonkAscensions.GetPlayerRank(
                        MonkAscensions.FlowingDefenseId);
                reduction = Mathf.Clamp(reduction, 0f, 0.34f);
                remaining *= 1f - reduction;
            }
            if (ReedInTheWindActive &&
                damageType == GameData.DamageType.Physical)
                remaining *= 0.85f;
            if (EmptyBodyActive &&
                damageType != GameData.DamageType.Physical)
                remaining *= 0.75f;
            return Mathf.Max(1,
                Mathf.RoundToInt(remaining));
        }

        private static void TryCounteringPalm(Character attacker,
            GameData.DamageType damageType)
        {
            if (!CounteringPalmActive || _counterDamageDepth > 0 ||
                damageType != GameData.DamageType.Physical ||
                attacker == null || !attacker.Alive) return;
            Character player = GameData.PlayerControl?.Myself;
            if (player == null || Vector3.Distance(player.transform.position,
                    attacker.transform.position) > 4f) return;
            _counterUntil = 0f;
            RemoveEffect(GameData.PlayerStats,
                MonkCatalog.GetEffect("countering_palm"));
            MonkTechniqueDefinition definition =
                MonkCatalog.GetDefinition("countering_palm");
            int rawDamage = Mathf.Max(1, Mathf.RoundToInt(
                GameData.PlayerStats.Level * (definition?.Power ?? 3) *
                0.55f));
            int dealt;
            _counterDamageDepth++;
            _techniqueDamageDepth++;
            try
            {
                dealt = attacker.DamageMe(rawDamage, true,
                    GameData.DamageType.Physical, player, true, false);
            }
            finally
            {
                _techniqueDamageDepth = Mathf.Max(0,
                    _techniqueDamageDepth - 1);
                _counterDamageDepth--;
            }
            MonkAnimationDriver.PlayCounter(definition, player.transform,
                attacker.transform);
            if (dealt > 0)
                UpdateSocialLog.CombatLogAdd(new ChatLogLine(
                    $"Countering Palm answers {attacker.transform.name} " +
                    $"for {dealt} physical damage.",
                    ChatLogLine.LogType.PlayerHits));
        }

        internal static void ApplyFlurryDelay(Stats stats, bool mainHand)
        {
            if (!HundredFistsActive || stats != GameData.PlayerStats) return;
            FieldInfo field = mainHand ? ActualMainDelay : ActualOffDelay;
            if (field == null) return;
            float current = (float)field.GetValue(stats);
            field.SetValue(stats, Mathf.Max(1f, current * 0.75f));
        }

        internal static bool HasOpenHandDiscipline()
        {
            Skill passive = MonkCatalog.GetSkill("open_hand_discipline");
            return passive != null && MonkCatalog.IsMonkPlayer() &&
                GameData.PlayerControl?.Myself?.MySkills?.KnownSkills?
                    .Contains(passive) == true;
        }

        internal static void ModifyHandwrapAutoDamage(ref int damage,
            bool mainHand)
        {
            Item item = mainHand ? GameData.PlayerInv?.MH?.MyItem :
                GameData.PlayerInv?.OH?.MyItem;
            if (damage > 0 && HasOpenHandDiscipline() &&
                MonkGear.IsHandwrap(item))
            {
                float intrinsic = GetOpenHandLevelMultiplier(item,
                    GameData.PlayerStats?.Level ?? 1);
                damage = Mathf.Max(1, Mathf.RoundToInt(
                    damage * 1.05f * intrinsic));
            }
        }

        /// <summary>
        /// EverQuest-style intrinsic fist progression prevents the level-one
        /// training wraps from becoming a trap before masterwork handwraps are
        /// obtainable. Level-30+ handwraps already carry their progression in
        /// their real item damage and never receive this catch-up multiplier.
        /// </summary>
        internal static float GetOpenHandLevelMultiplier(Item handwrap,
            int level)
        {
            if (!MonkGear.IsHandwrap(handwrap) ||
                handwrap.ItemLevel >= 30)
                return 1f;
            return Mathf.Clamp(1f + Mathf.Max(0,
                level - Mathf.Max(1, handwrap.ItemLevel)) * 0.35f,
                1f, 13f);
        }

        internal static void PlayHandwrapAutoAnimation(bool mainHand)
        {
            ApplyFlurryDelay(GameData.PlayerStats, mainHand);
            if (!UsesHandwrapAutoAnimation(mainHand)) return;
            Transform caster = GameData.PlayerControl?.transform;
            Transform target = GameData.PlayerControl?.CurrentTarget?.transform;
            if (caster == null) return;
            Animator animator = caster.GetComponent<Animator>();
            SuppressNativeWeaponAutoAnimation(animator);
            MonkAnimationDriver.PlayAutoPunch(caster, target, mainHand);
        }

        internal static bool UsesHandwrapAutoAnimation(bool mainHand)
        {
            Item item = mainHand ? GameData.PlayerInv?.MH?.MyItem :
                GameData.PlayerInv?.OH?.MyItem;
            return HandToHandReadyIdle.IsFistWeapon(item);
        }

        internal static void SuppressNativeWeaponAutoAnimation(
            Animator animator)
        {
            if (animator == null) return;
            animator.ResetTrigger("MeleeSwing");
            animator.ResetTrigger("DualWield");
            // Erenshor drives follow-up melee swings with booleans rather than
            // triggers. Leaving either one set layers the native one-handed
            // weapon motion beneath the authored cross, which reads as the
            // Monk swinging an invisible sword during double attacks.
            animator.SetBool("DoubleAttack", false);
            animator.SetBool("OHDoubleAttack", false);
        }

        internal static int PreviewTechniqueDamageForTesting(int damage)
        {
            _techniqueDamageDepth++;
            try
            {
                return ModifyTechniqueDamage(damage,
                    GameData.PlayerControl?.Myself);
            }
            finally
            {
                _techniqueDamageDepth = Mathf.Max(0,
                    _techniqueDamageDepth - 1);
            }
        }

        internal static float ReadAttackDelayForTesting(Stats stats,
            bool mainHand)
        {
            FieldInfo field = mainHand ? ActualMainDelay : ActualOffDelay;
            if (stats == null || field == null) return -1f;
            return (float)field.GetValue(stats);
        }

        internal static void ResetCharacterState()
        {
            Character player = GameData.PlayerControl?.Myself;
            // Unity objects can compare CLR-non-null after destruction, so
            // null-conditional access is not sufficient during scene teardown.
            if (player != null && player.Alive)
            {
                Animator animator = player.GetComponent<Animator>();
                animator?.SetBool("Dead", false);
            }
            _feignUntil = 0f;
            _stoneUntil = 0f;
            _flurryUntil = 0f;
            _counterUntil = 0f;
            _reedUntil = 0f;
            _emptyUntil = 0f;
            _focusUntil = 0f;
            _centeredUntil = 0f;
            _centeredNextTick = 0f;
            _centeredTicks = 0;
            _centeredHealPerTick = 0;
            _harmonyUntil = 0f;
            _harmonyNextTick = 0f;
            _harmonyTicks = 0;
            _harmonyHealPerTick = 0;
            _techniqueDamageDepth = 0;
            _counterDamageDepth = 0;
            LastJabAutoDamage = 0;
            LastJabBonusDamage = 0;
            LastJabCompositeDamage = 0;
        }

        internal static string Describe()
        {
            return $"feign={(IsFeigning ? "active" : "off")}, " +
                $"stone={(StoneStanceActive ? "active" : "off")}, " +
                $"flurry={(HundredFistsActive ? "active" : "off")}, " +
                $"counter={(CounteringPalmActive ? "active" : "off")}, " +
                $"reed={(ReedInTheWindActive ? "active" : "off")}, " +
                $"empty={(EmptyBodyActive ? "active" : "off")}, " +
                $"focus={(FivePointFocusActive ? "active" : "off")}";
        }
    }

    [HarmonyPatch]
    internal static class Patch_MonkUseSkill
    {
        private static IEnumerable<MethodBase> TargetMethods()
        {
            yield return AccessTools.Method(typeof(UseSkill), nameof(UseSkill.DoSkill));
            yield return AccessTools.Method(typeof(UseSkill), nameof(UseSkill.DoSkillNoChecks));
        }

        private static bool Prefix(UseSkill __instance, Skill _skill,
            Character _target, ref bool __result,
            out MonkRuntime.SkillUseState __state) =>
            MonkRuntime.PrepareSkillUse(__instance, _skill, _target,
                ref __result, out __state);

        private static void Postfix(bool __result,
            MonkRuntime.SkillUseState __state) =>
            MonkRuntime.CompleteSkillUse(__state, __result);

        private static Exception Finalizer(Exception __exception,
            MonkRuntime.SkillUseState __state)
        {
            if (__exception != null)
                MonkRuntime.CompleteSkillUse(__state, false);
            return __exception;
        }
    }

    [HarmonyPatch(typeof(CastSpell), nameof(CastSpell.StartSpell),
        new[] { typeof(Spell), typeof(Stats) })]
    internal static class Patch_MonkCastTechnique
    {
        private static bool Prefix(CastSpell __instance, Spell _spell,
            Stats _target, ref bool __result) =>
            MonkRuntime.PrepareSpellUse(__instance, _spell, _target,
                ref __result);
    }

    [HarmonyPatch(typeof(Character), "DamageMe",
        new[]
        {
            typeof(int), typeof(bool), typeof(GameData.DamageType),
            typeof(Character), typeof(bool), typeof(bool), typeof(int)
        })]
    internal static class Patch_MonkDamageRules
    {
        private static void Prefix(Character __instance, ref int _incdmg,
            GameData.DamageType _dmgType, Character _attacker)
        {
            _incdmg = MonkRuntime.ModifyTechniqueDamage(_incdmg, _attacker,
                __instance);
            _incdmg = MonkRuntime.ModifyIncomingDamage(_incdmg, __instance,
                _dmgType, _attacker);
        }
    }

    [HarmonyPatch(typeof(NPC), nameof(NPC.AggroOn))]
    internal static class Patch_MonkFeignAggroOn
    {
        private static bool Prefix(Character tar) =>
            !MonkRuntime.BlocksAggro(tar);
    }

    [HarmonyPatch(typeof(NPC), nameof(NPC.ForceAggroOn))]
    internal static class Patch_MonkFeignForceAggro
    {
        private static bool Prefix(Character tar) =>
            !MonkRuntime.BlocksAggro(tar);
    }

    [HarmonyPatch(typeof(NPC), nameof(NPC.ManageAggro))]
    internal static class Patch_MonkFeignManageAggro
    {
        private static bool Prefix(Character _attacker) =>
            !MonkRuntime.BlocksAggro(_attacker);
    }

    [HarmonyPatch(typeof(PlayerCombat), "HandleDamageResult")]
    internal static class Patch_MonkOpenHandDamage
    {
        private static void Prefix(ref int dmg, bool isMainHand) =>
            MonkRuntime.ModifyHandwrapAutoDamage(ref dmg, isMainHand);
    }

    [HarmonyPatch(typeof(PlayerCombat), "PerformAttacks")]
    internal static class Patch_MonkAutoAttackAnimation
    {
        private static void Postfix(bool isMainHand)
        {
            try
            {
                // AttackSyncFix owns the normal handwrap route. Keep this
                // postfix as a safe fallback if another Harmony prefix skipped
                // that replacement after range validation.
                if (Patches.Patch_PerformAttacks
                        .HandwrapAnimationEligible &&
                    !Patches.Patch_PerformAttacks
                        .HandwrapAnimationHandled)
                    MonkRuntime.PlayHandwrapAutoAnimation(isMainHand);
            }
            finally
            {
                Patches.Patch_PerformAttacks
                    .ResetHandwrapAnimationContext();
            }
        }
    }
}
