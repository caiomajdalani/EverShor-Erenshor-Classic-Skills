# 1.9.2: prevent Sim Monk auto-attack leakage and isolate combat state

Follow-up to merged PR #1. Base: upstream main at 28516e6, containing the native Lunaris 1.9.0 migration. Plugin, project and manifest now consistently identify version 1.9.2.

Community reports described buggy Monk punches and approach, unexpected auto-attack during interactions, and a Necromancer apparently using Resonant Palm. This change addresses concrete isolation and execution-path problems found during source review; it does not claim every reported symptom has been reproduced or resolved.

## Monk corrections

- Make the live player class authoritative over stale saved-slot labels. Reject custom techniques for non-Monk actors and while Monk is disabled; handle null catalog lookup safely.
- Resolve Sim targets from that Sim's own NPC aggro state, never from the player's selected target. Route Sim casts through the NPC skill path.
- Apply validation/animation hooks to both DoSkill and DoSkillNoChecks, the execution path used by NPCs.
- Check caster/target life, self-targeting, reach and Sim aggro-target ownership. Prevent Sim utility techniques from sharing the player's stance timers.
- Only the actual local player caster updates player hotkey cooldowns and receives technique-training credit. Existing saved ability lists are not deleted.

## Additional 1.9.2 corrections

- Fix the unconditional GameData.PlayerCombat.ForceAttackOn call in Resonant Palm. A Sim using that technique could enable the player's auto-attack while the player was interacting with the guild menu or inventory. Only the real local caster may auto-engage, respecting AutoEngageAttackOnSkill.
- Attribute Resonant Palm and Pummel follow-up messages to the real caster and the appropriate PlayerHits/OthersHits category. Sim hits no longer appear anonymously as player damage.
- Validate follow-ups against the actual caster's Monk class instead of the local player's class. Cancel queued punches after retargeting, death, leaving melee reach or disabling Monk.
- Add ten policy regressions for follow-up cancellation and the caster/preference auto-engage truth table. Damage tuning and learned-ability save data are preserved.

## Complete player guide

Restore the README as a gameplay/mod guide covering 25 skills, training, crafting, SimCraft, economy, bags, corpse recovery, trainers, class progressions, Thrall equipment, commands and settings. Verify the lists of 35 Necromancer spells, 32 Monk techniques, shared native skills and eight Ascensions per class against source. Move optional QA commands into a separate reference and retain original branding/credits.

## Validation and remaining work

Local Release/Debug builds pass with zero errors and two existing MSB3277 dependency-version warnings. Thirty-three linked production-policy regression cases, version agreement, 644 icon checks, 86 literal Harmony target names and maintained documentation links are checked by the scripts. Source-only validation is also run against the source archive without game dependencies.

Community testing is ongoing and has reported partial improvements. A complete reproducible in-game matrix is not claimed. Sim approach/positioning, animation quality across rigs and the precise origin of reported cross-class spellbook contamination remain pending reproduction. The existing runtime mitigation does not establish that all Unity crashes are fixed.

This PR now advances the version to 1.9.2, incorporating the previously proposed 1.9.1 fixes so the current corrections can be reviewed together. Further fixes should be delivered as new versioned releases. No game/runtime DLLs, private saves, diagnostics binaries or inspection-tool/decompiled game sources are included.
