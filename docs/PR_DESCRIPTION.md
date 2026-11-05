# 1.9.1: isolate Monk combat state and restore the complete gameplay README

Follow-up to merged PR #1. Base: upstream main at 28516e6, containing the native Lunaris 1.9.0 migration. Plugin, project and manifest now consistently identify version 1.9.1.

Community reports described buggy Monk punches and approach, unexpected auto-attack during interactions, and a Necromancer apparently using Resonant Palm. This change addresses concrete isolation and execution-path problems found during source review; it does not claim every reported symptom has been reproduced or resolved.

## Monk corrections

- Make the live player class authoritative over stale saved-slot labels. Reject custom techniques for non-Monk actors and while Monk is disabled; handle null catalog lookup safely.
- Resolve Sim targets from that Sim's own NPC aggro state, never from the player's selected target. Route Sim casts through the NPC skill path.
- Apply validation/animation hooks to both DoSkill and DoSkillNoChecks, the execution path used by NPCs.
- Check caster/target life, self-targeting, reach and Sim aggro-target ownership. Prevent Sim utility techniques from sharing the player's stance timers.
- Only the actual local player caster updates player hotkey cooldowns and receives technique-training credit. Existing saved ability lists are not deleted.

## Complete player guide

Restore the README as a gameplay/mod guide covering 25 skills, training, crafting, SimCraft, economy, bags, corpse recovery, trainers, class progressions, Thrall equipment, commands and settings. Verify the lists of 35 Necromancer spells, 32 Monk techniques, shared native skills and eight Ascensions per class against source. Move optional QA commands into a separate reference and retain original branding/credits.

## Validation and remaining work

Local Release/Debug builds pass with zero errors and two existing MSB3277 dependency-version warnings. Twenty-three linked production-policy regression cases, version agreement, 644 icon checks, 86 literal Harmony target names and maintained documentation links are checked by the scripts. Source-only validation is also run against the source archive without game dependencies.

Community testing is ongoing and has reported partial improvements. A complete reproducible in-game matrix is not claimed. Sim approach/positioning, animation quality across rigs and the precise origin of reported cross-class spellbook contamination remain pending reproduction. The existing runtime mitigation does not establish that all Unity crashes are fixed.

This PR advances the version to 1.9.1 so the current corrections can be reviewed together. Further fixes should be delivered as new versioned releases. No game/runtime DLLs, private saves, diagnostics binaries or inspection-tool/decompiled game sources are included.
