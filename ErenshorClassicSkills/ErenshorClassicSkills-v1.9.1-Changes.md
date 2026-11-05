# EverShor 1.9.1 — Monk isolation fixes and complete player guide

Follow-up to upstream 1.9.0, merged in PR #1. Plugin, project and manifest version: 1.9.1. Original plugin GUID, mod branding and author credits are preserved.

## Monk corrections

- Prefer the live character class over a stale saved-slot Monk label.
- Reject custom Monk technique execution for non-Monk actors or when Monk is disabled; handle null skill/spell lookup safely.
- Check life state, self-targeting and technique reach. Sim techniques require the Sim's own active aggro target.
- Resolve a Sim's fallback target from its own NPC instead of the player's selected target.
- Execute Sim techniques through the NPC skill path and apply the validation/animation hooks to both DoSkill and DoSkillNoChecks.
- Block AI utility techniques that depend on local-player stance timers. Only the actual player caster updates player hotkeys and receives technique-training credit.

## Documentation

Restore the main README as a gameplay guide covering all skills, crafting, SimCraft, materials, custom-class progressions, equipment, trainers, commands and settings. Verify 35 Necromancer spells, 32 Monk techniques, shared skill lists and eight Ascensions per class against source. Keep optional testing/developer instructions separate.

## Validation and known issues

Community testing is in progress and has reported partial improvements. Those reports are not a complete reproducible game-test matrix. Automated regressions cover 23 combat-policy cases; build/reference/icon/link checks are recorded in docs/VALIDATION.md.

Sim Monk approach/positioning, animation quality across rigs and the precise cause of reported cross-class spellbook contamination still require in-game reproduction. Existing save ability lists are not deleted. Two existing MSB3277 Lunaris reference warnings remain. Further corrections should receive new release versions rather than replacing this release's artifacts.
