using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using UnityEngine;

namespace ErenshorSkills.Necromancer
{
    internal static class NecromancerRuntime
    {
        internal const float PetHealMaxHealthFraction = 0.25f;
        private sealed class PendingCast
        {
            internal Spell Spell;
            internal Stats Target;
        }

        internal sealed class ResolutionContext
        {
            internal Spell Spell;
            internal CastSpell Source;
            internal Stats Target;
        }

        private sealed class CovenantState
        {
            internal Spell Spell;
            internal NecromancerSpellDefinition Definition;
            internal Character Caster;
            internal float NextTickAt;
            internal float EndsAt;
            internal int TicksRemaining;
        }

        private static readonly Dictionary<CastSpell, PendingCast> Pending =
            new Dictionary<CastSpell, PendingCast>();
        private static readonly FieldInfo SpellSourceField = AccessTools.Field(
            typeof(SpellVessel), "SpellSource");
        private static readonly FieldInfo TargetField = AccessTools.Field(
            typeof(SpellVessel), "targ");

        private static bool _harmshieldActive;
        private static bool _harmshieldPreviousInvulnerable;
        private static Vector3 _harmshieldAnchor;
        private static CovenantState _covenant;

        internal static int ResolutionCount { get; private set; }
        internal static Spell LastResolvedSpell { get; private set; }
        internal static string LastResolutionError { get; private set; }
        internal static int CastStartCount { get; private set; }
        internal static Spell LastCastStartedSpell { get; private set; }
        internal static CastSpell LastCastSource { get; private set; }
        internal static float LastCastTicks { get; private set; }
        internal static bool LastCastBarReady { get; private set; }
        internal static bool LastCastAuraReady { get; private set; }
        internal static bool LastOrbitingSkullsReady { get; private set; }
        internal static bool LastSummonSigilReady { get; private set; }
        internal static string LastCastStartError { get; private set; }

        internal static void ResetResolutionProbe()
        {
            ResolutionCount = 0;
            LastResolvedSpell = null;
            LastResolutionError = null;
            CastStartCount = 0;
            LastCastStartedSpell = null;
            LastCastSource = null;
            LastCastTicks = 0f;
            LastCastBarReady = false;
            LastCastAuraReady = false;
            LastOrbitingSkullsReady = false;
            LastSummonSigilReady = false;
            LastCastStartError = null;
        }

        internal static bool HasActiveCovenant(Stats stats)
        {
            return stats != null && _covenant != null &&
                _covenant.Caster == stats.Myself &&
                Time.time < _covenant.EndsAt &&
                _covenant.TicksRemaining > 0;
        }

        internal static bool PrepareCast(CastSpell caster, Spell spell,
            ref Stats target, out string denial)
        {
            denial = null;
            if (!NecromancerCatalog.TryGetDefinition(spell, out var definition) ||
                definition.HiddenEffect) return true;
            if (!NecromancerCatalog.EnsureRuntimeSpellBindings(spell,
                    out string bindingError))
            {
                denial = "That spell is not ready yet (" + bindingError +
                    "). Try again.";
                return false;
            }
            NecromancerPetMarker petMarker = caster?.MyChar != null
                ? caster.MyChar.GetComponent<NecromancerPetMarker>() : null;
            if (petMarker != null)
            {
                NPC pet = caster.MyChar.GetComponent<NPC>();
                bool assigned = pet != null &&
                    (pet.MyAttackSpells?.Contains(spell) == true ||
                     pet.MyBuffSpells?.Contains(spell) == true ||
                     pet.MyTauntSpell?.Contains(spell) == true);
                if (definition.Kind != NecromancerSpellKind.Native ||
                    !assigned || definition.Fear || definition.Root ||
                    definition.SelfOnly)
                {
                    denial = "That spell is not available to this Bone Thrall.";
                    return false;
                }
            }
            else if (!NecromancerCatalog.IsNecromancerClass(
                         caster?.MyChar?.MyStats?.CharacterClass))
            {
                denial = "Only a Necromancer can cast that spell.";
                return false;
            }

            Stats intended = target;
            if (definition.Kind == NecromancerSpellKind.PetHeal ||
                definition.Kind == NecromancerSpellKind.PetBuff)
            {
                intended = GetOwnedPetStats(caster);
                if (intended == null)
                {
                    denial = "You have no living bone thrall to affect.";
                    return false;
                }
                target = intended;
            }
            else if (definition.Kind == NecromancerSpellKind.ReclaimPet)
            {
                if (GetOwnedPetStats(caster) == null)
                {
                    denial = "You have no living bone thrall to reclaim.";
                    return false;
                }
            }
            else if (definition.Kind == NecromancerSpellKind.SoulTransfusion)
            {
                intended = intended ?? GameData.PlayerControl?.CurrentTarget?.MyStats;
                if (intended == null || intended == caster.MyChar.MyStats ||
                    !intended.PlayerOrSimPlayer)
                {
                    denial = "Target another player or grouped SimPlayer.";
                    return false;
                }
                Character intendedCharacter = intended.Myself;
                if (intendedCharacter?.GetComponent<SimPlayer>() != null &&
                    intendedCharacter.MyNPC?.InGroup != true)
                {
                    denial = "Soul Transfusion only works on grouped SimPlayers.";
                    return false;
                }
                target = intended;
            }
            else if (definition.Kind == NecromancerSpellKind.HealthToMana)
            {
                if (_covenant != null &&
                    _covenant.Caster == caster.MyChar &&
                    Time.time < _covenant.EndsAt)
                {
                    denial = "A health-to-mana covenant is already active.";
                    return false;
                }
                int healthCost = Mathf.Abs(definition.Hp);
                int reserve = Mathf.CeilToInt(
                    caster.MyChar.MyStats.CurrentMaxHP * 0.25f);
                if (caster.MyChar.MyStats.CurrentHP - healthCost < reserve)
                {
                    denial = "You need more than 25% health to begin that covenant.";
                    return false;
                }
            }
            else if (definition.Kind == NecromancerSpellKind.SummonPet &&
                caster.MyChar.MyCharmedNPC != null)
            {
                denial = "You already command a companion.";
                return false;
            }

            if (definition.SelfOnly || target == null)
                target = caster.MyChar.MyStats;
            Pending[caster] = new PendingCast { Spell = spell, Target = intended ?? target };
            return true;
        }

        internal static void OnCastStarted(CastSpell source, Spell spell,
            float castTicks)
        {
            if (!NecromancerCatalog.TryGetDefinition(spell,
                    out NecromancerSpellDefinition definition))
                return;

            CastStartCount++;
            LastCastStartedSpell = spell;
            LastCastSource = source;
            LastCastTicks = castTicks;
            LastCastBarReady = false;
            LastCastAuraReady = false;
            LastOrbitingSkullsReady = false;
            LastSummonSigilReady = false;
            LastCastStartError = null;

            try
            {
                bool playerSource = source?.MyChar != null &&
                    ReferenceEquals(source.MyChar,
                        GameData.PlayerControl?.Myself);
                if (playerSource)
                {
                    CastBar bar = GameData.CB;
                    if (bar?.castBar != null &&
                        !bar.castBar.activeSelf)
                        bar.NewCast(spell.SpellName);
                    LastCastBarReady =
                        bar?.castBar != null && bar.castBar.activeSelf;
                }

                float castSeconds = Mathf.Max(0.45f, castTicks / 60f);
                LastCastAuraReady = NecromancerVfx.PlayCastAura(
                    definition, source, spell, castSeconds,
                    out bool summonSigil);
                NecromancerAudio.PlayCast(definition, source.transform);
                LastOrbitingSkullsReady =
                    NecromancerVfx.HasActiveOrbitingSkulls(
                        source?.transform);
                LastSummonSigilReady = summonSigil;
                if (SkillsPlugin.CfgEnableTestMode?.Value == true)
                    SkillsPlugin.Log.LogInfo(
                        $"[Necromancer Cast Probe] spell={spell.SpellName}; " +
                        $"ticks={castTicks:0.##}; player={playerSource}; " +
                        $"bar={LastCastBarReady}; " +
                        $"evilAura={LastCastAuraReady}; " +
                        $"skulls={LastOrbitingSkullsReady}; " +
                        $"demonicSigil={LastSummonSigilReady}");
            }
            catch (Exception ex)
            {
                LastCastStartError = ex.Message;
                SkillsPlugin.Log.LogError(
                    $"Necromancer spell '{spell?.SpellName}' cast-start " +
                    $"VFX failed: {ex}");
            }
        }

        internal static ResolutionContext CaptureResolutionContext(
            SpellVessel vessel)
        {
            return new ResolutionContext
            {
                Spell = vessel?.spell,
                Source = SpellSourceField?.GetValue(vessel) as CastSpell,
                Target = TargetField?.GetValue(vessel) as Stats
            };
        }

        internal static void AfterResolve(SpellVessel vessel,
            ResolutionContext context)
        {
            Spell spell = context?.Spell ?? vessel?.spell;
            if (!NecromancerCatalog.TryGetDefinition(spell, out var definition))
                return;
            CastSpell source = context?.Source ??
                SpellSourceField?.GetValue(vessel) as CastSpell;
            Stats vesselTarget = context?.Target ??
                TargetField?.GetValue(vessel) as Stats;
            if (source?.MyChar?.MyStats == null) return;

            LastResolvedSpell = spell;
            LastResolutionError = null;

            Stats intended = vesselTarget;
            if (Pending.TryGetValue(source, out PendingCast pending) &&
                ReferenceEquals(pending.Spell, spell))
            {
                intended = pending.Target ?? intended;
                Pending.Remove(source);
            }

            try
            {
                switch (definition.Kind)
                {
                    case NecromancerSpellKind.SummonPet:
                        // The ground burst belongs under the thrall, not the
                        // caster, so it is raised from the pet coroutine once
                        // the native summon has actually placed the skeleton.
                        NecromancerPets.ConfigureSummonedPet(source.MyChar,
                            definition.PetTier, definition);
                        break;
                    case NecromancerSpellKind.PetHeal:
                        HealPet(source, definition);
                        break;
                    case NecromancerSpellKind.HealthToMana:
                        BeginCovenant(source, spell, definition);
                        break;
                    case NecromancerSpellKind.ReclaimPet:
                        ReclaimPet(source, definition);
                        break;
                    case NecromancerSpellKind.FeignDeath:
                        ApplyFeignDeath(source, spell);
                        break;
                    case NecromancerSpellKind.SoulTransfusion:
                        TransferMana(source, intended, definition, spell);
                        break;
                    case NecromancerSpellKind.PetBuff:
                        BuffPet(source, spell);
                        break;
                    case NecromancerSpellKind.Harmshield:
                        ApplyHarmshield(source, spell);
                        break;
                }

                if (definition.Pbaoe)
                    EnsurePbaoeAppliedEffect(source, definition);

                Transform visualTarget = intended?.transform ?? vesselTarget?.transform;
                if (definition.Kind == NecromancerSpellKind.PetHeal ||
                    definition.Kind == NecromancerSpellKind.PetBuff)
                    visualTarget = GetOwnedPetStats(source)?.transform;
                if (definition.Kind != NecromancerSpellKind.SummonPet)
                    NecromancerAudio.PlayResolve(definition,
                        visualTarget != null
                            ? visualTarget.position + Vector3.up * 0.72f
                            : source.transform.position + Vector3.up * 0.72f);
                if (!definition.SelfOnly && !definition.Pbaoe)
                    NecromancerVfx.PlayProjectile(definition,
                        source.transform, visualTarget);
            }
            catch (Exception ex)
            {
                LastResolutionError = ex.Message;
                SkillsPlugin.Log.LogError(
                    $"Necromancer spell '{spell?.SpellName}' custom resolution failed: {ex}");
            }
            finally
            {
                ResolutionCount++;
            }
        }

        /// <summary>
        /// The native PBAE collector can miss a character that already
        /// overlaps its trigger on the frame it is created. Native particle
        /// foundations used to mask that race by applying the linked effect a
        /// second way. Custom-only VFX instead guarantee the authored status
        /// here, without repeating native damage or aggro.
        /// </summary>
        private static void EnsurePbaoeAppliedEffect(CastSpell source,
            NecromancerSpellDefinition definition)
        {
            if (source?.MyChar == null || definition == null ||
                string.IsNullOrEmpty(definition.AppliedEffectKey)) return;
            Spell effect = NecromancerCatalog.GetSpell(
                definition.AppliedEffectKey);
            if (effect == null) return;

            var seen = new HashSet<Stats>();
            float radius = Mathf.Max(0.5f, definition.Range);
            foreach (Collider collider in Physics.OverlapSphere(
                source.transform.position, radius,
                Physics.AllLayers, QueryTriggerInteraction.Collide))
            {
                Character candidate =
                    collider?.GetComponentInParent<Character>();
                Stats stats = candidate?.MyStats;
                if (candidate == null || stats == null ||
                    candidate == source.MyChar || !candidate.Alive ||
                    candidate.Invulnerable || stats.Charmed ||
                    stats.PlayerOrSimPlayer || !seen.Add(stats))
                    continue;
                if (candidate.MyFaction == source.MyChar.MyFaction ||
                    source.MyChar.Allies.Contains(candidate.MyFaction))
                    continue;
                if (!stats.CheckForStatus(effect))
                    stats.AddStatusEffect(effect, source.isPlayer, 0,
                        source.MyChar);
            }
        }

        private static Stats GetOwnedPetStats(CastSpell source)
        {
            NPC pet = source?.MyChar?.MyCharmedNPC;
            if (pet == null || pet.GetComponent<NecromancerPetMarker>() == null)
                return null;
            Character character = pet.GetComponent<Character>();
            if (character == null || !character.Alive) return null;
            return pet.GetComponent<Stats>();
        }

        private static void HealPet(CastSpell source,
            NecromancerSpellDefinition definition)
        {
            Stats pet = GetOwnedPetStats(source);
            if (pet == null) return;
            int amount = Mathf.Max(definition.CustomAmount +
                Mathf.Max(0, source.MyChar.MyStats.Level - definition.Level) * 12,
                Mathf.RoundToInt(pet.CurrentMaxHP *
                    PetHealMaxHealthFraction));
            // Patch_AlterationHeal only covers the five-argument Stats.HealMe,
            // and this heal uses the flat overload so it can post its own
            // combat line without the native one duplicating it. Apply the
            // Alteration bonus here so the skill still pays out.
            amount = Mathf.Max(1, Mathf.RoundToInt(
                amount * Skills.MagicSkills.GetHealMultiplier()));
            int before = pet.CurrentHP;
            pet.HealMe(amount);
            int restored = pet.CurrentHP - before;
            ChatHelper.SendCombat(
                $"Mend Bones restores {restored} health to {pet.MyName}.",
                "#A8E6A3");
        }

        private static void BeginCovenant(CastSpell source, Spell spell,
            NecromancerSpellDefinition definition)
        {
            _covenant = new CovenantState
            {
                Spell = spell,
                Definition = definition,
                Caster = source.MyChar,
                NextTickAt = Time.time,
                EndsAt = Time.time + Mathf.Max(3f,
                    definition.DurationTicks * 3f),
                TicksRemaining = Mathf.Max(1, definition.DurationTicks)
            };
            NecromancerVfx.PlayPersistent(definition, source.transform,
                Mathf.Max(3f, definition.DurationTicks * 3f));
            TickCovenant();
            ChatHelper.SendCombat(
                $"{spell.SpellName} begins sacrificing health to recover mana.",
                "#C96B7B");
        }

        private static void TickCovenant()
        {
            CovenantState state = _covenant;
            Stats stats = state?.Caster?.MyStats;
            if (state == null || stats == null || !state.Caster.Alive ||
                state.Caster != GameData.PlayerControl?.Myself ||
                !NecromancerCatalog.IsNecromancerClass(stats.CharacterClass))
            {
                _covenant = null;
                return;
            }

            while (state.TicksRemaining > 0 && Time.time >= state.NextTickAt)
            {
                int healthCost = Mathf.Abs(state.Definition.Hp);
                int reserve = Mathf.Max(1,
                    Mathf.CeilToInt(stats.CurrentMaxHP * 0.25f));
                if (healthCost > 0 && stats.CurrentHP - healthCost >= reserve)
                {
                    stats.CurrentHP -= healthCost;
                    stats.RecentDmg = Mathf.Max(stats.RecentDmg, 240f);
                    stats.CurrentMana = Mathf.Min(stats.GetCurrentMaxMana(),
                        stats.CurrentMana + state.Definition.CustomAmount);
                }
                state.TicksRemaining--;
                state.NextTickAt += 3f;
            }
            if (state.TicksRemaining <= 0 && Time.time >= state.EndsAt)
                _covenant = null;
        }

        private static void ReclaimPet(CastSpell source,
            NecromancerSpellDefinition definition)
        {
            NPC pet = source.MyChar.MyCharmedNPC;
            if (pet == null || pet.GetComponent<NecromancerPetMarker>() == null) return;
            int restored = Mathf.RoundToInt(source.MyChar.MyStats.GetCurrentMaxMana() *
                definition.CustomAmount / 100f);
            int before = source.MyChar.MyStats.CurrentMana;
            source.MyChar.MyStats.CurrentMana = Mathf.Min(
                source.MyChar.MyStats.GetCurrentMaxMana(), before + restored);
            restored = source.MyChar.MyStats.CurrentMana - before;
            NecromancerPets.ReleasePetReference(pet, "reclaimed");
            UnityEngine.Object.Destroy(pet.gameObject);
            ChatHelper.SendCombat(
                $"The thrall returns {restored} mana. Its equipment remains safely stored.",
                "#C8D0B4");
        }

        private static void ApplyFeignDeath(CastSpell source, Spell spell)
        {
            Stats stats = source.MyChar.MyStats;
            int slot = stats.AddStatusEffect(spell, true, 0, source.MyChar);
            if (slot < 0) return;
            ClearPlayerAggro();
            ChatHelper.SendCombat(
                "You collapse into a deathly guise.", "#AEB3BD");
        }

        private static void TransferMana(CastSpell source, Stats target,
            NecromancerSpellDefinition definition, Spell spell)
        {
            if (target == null || target == source.MyChar.MyStats) return;
            int before = target.CurrentMana;
            target.HealMe(spell, definition.CustomAmount, false, true, source.MyChar);
            int restored = target.CurrentMana - before;
            ChatHelper.SendCombat(
                $"Soul Transfusion restores {restored} mana to {target.MyName}.",
                "#6EB5DE");
        }

        private static void BuffPet(CastSpell source, Spell spell)
        {
            Stats pet = GetOwnedPetStats(source);
            if (pet == null) return;
            int beforeMax = pet.CurrentMaxHP;
            int slot = pet.AddStatusEffect(spell, true, 0, source.MyChar);
            if (slot < 0) return;
            int maxIncrease = Mathf.Max(0, pet.CurrentMaxHP - beforeMax);
            pet.CurrentHP = Mathf.Min(pet.CurrentMaxHP, pet.CurrentHP + maxIncrease);
            ChatHelper.SendCombat(
                "Your bond strengthens the thrall.", "#88C9A4");
        }

        private static void ApplyHarmshield(CastSpell source, Spell spell)
        {
            Stats stats = source.MyChar.MyStats;
            int slot = stats.AddStatusEffect(spell, true, 0, source.MyChar);
            if (slot < 0) return;
            _harmshieldPreviousInvulnerable = source.MyChar.Invulnerable;
            _harmshieldActive = true;
            _harmshieldAnchor = source.transform.position;
            source.MyChar.Invulnerable = true;
            try { GameData.PlayerCombat?.ForceAttackOff(); } catch { }
            ChatHelper.SendCombat(
                "Harmshield seals you from all harm.", "#D0D8EF");
        }

        internal static void Tick()
        {
            if (_covenant != null) TickCovenant();
            if (!_harmshieldActive) return;
            Character player = GameData.PlayerControl?.Myself;
            Spell harmshield = NecromancerCatalog.GetSpell("harmshield");
            bool stillActive = player?.MyStats != null && harmshield != null &&
                player.MyStats.CheckForStatus(harmshield);
            if (!stillActive)
            {
                if (player != null)
                    player.Invulnerable = _harmshieldPreviousInvulnerable;
                _harmshieldActive = false;
                return;
            }
            player.Invulnerable = true;
            if (GameData.PlayerControl.transform.position != _harmshieldAnchor)
                GameData.PlayerControl.transform.position = _harmshieldAnchor;
        }

        internal static bool BlocksAggro(Character target)
        {
            if (target == null || target != GameData.PlayerControl?.Myself) return false;
            Spell guise = NecromancerCatalog.GetSpell("deathly_guise");
            return guise != null && target.MyStats?.CheckForStatus(guise) == true;
        }

        internal static void ClearPlayerAggro()
        {
            Character player = GameData.PlayerControl?.Myself;
            if (player == null) return;
            foreach (NPC npc in UnityEngine.Object.FindObjectsOfType<NPC>())
            {
                if (npc == null || npc.SimPlayer) continue;
                if (npc.CurrentAggroTarget == player) npc.CurrentAggroTarget = null;
                if (npc.AggroTable != null)
                    npc.AggroTable.RemoveAll(slot => slot == null || slot.Player == player);
                GameData.AttackingPlayer?.Remove(npc);
            }
        }

        internal static void OnStatusApplied(Stats target, Spell spell, int slot)
        {
            if (slot < 0 || target == null ||
                !NecromancerCatalog.TryGetDefinition(spell, out var definition) ||
                definition.DurationTicks <= 0) return;
            NecromancerVfx.PlayPersistent(definition, target.transform,
                definition.DurationTicks * 3f);
        }

        internal static void ResetCharacterState()
        {
            Character player = GameData.PlayerControl?.Myself;
            if (_harmshieldActive && player != null)
                player.Invulnerable = _harmshieldPreviousInvulnerable;
            _harmshieldActive = false;
            _covenant = null;
            Pending.Clear();
            ResetResolutionProbe();
        }

        internal static void CancelCovenantForTesting(Stats stats)
        {
            if (_covenant?.Caster?.MyStats == stats) _covenant = null;
        }
    }
}
