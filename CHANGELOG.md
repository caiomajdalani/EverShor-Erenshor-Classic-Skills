# Changelog

## 1.9.2 — local previews: Monk follow-up and auto-attack isolation (2026-11-04)

- Attribute Sim follow-up messages to their actual NPC name and OthersHits instead of anonymous PlayerHits.
- Validate the actual caster's Monk class and combat state rather than requiring the local player to be a Monk.
- Cancel pending punches on retarget, death, leaving melee range or disabling the class.
- Preview 2 fixes Resonant Palm unconditionally enabling local-player auto-attack when cast by a Sim. Only the actual player may auto-engage, respecting the native preference.
- Preview 2 also attributes Resonant Palm damage to its real caster and suppresses player-facing cast-failure messages for Sims.
- Add six follow-up and four auto-engage policy regressions (33 total). Preserve damage tuning and learned-ability data.
- See docs/MONK-PUMMEL-REPORT.md for evidence, limitations and the tester scenario. This preview is separate from PR #2 / 1.9.1.


## 1.9.1 — Monk corrections and complete player guide (2026-11-04)

Follow-up to merged upstream PR #1. Full player-guide restoration and source-checked class progressions are included with the Monk corrections. Future fixes will receive new versions.

- Make the live player class authoritative over stale saved-slot class labels.
- Make Monk skill/spell lookups null-safe and reject use by other classes or while Monk is disabled.
- Validate caster/target life, self-targeting, reach and Sim ownership of the current aggro target.
- Route Sim spellbook techniques through the NPC skill path; never fall back to the player's selected target or alter the player's hotkey cooldowns.
- Apply the Monk validation/animation hooks to DoSkillNoChecks as well as DoSkill. Block Sim utility techniques that use local-player stance timers.
- Restrict technique training credit to the actual local caster. Add 11 Monk combat-policy regressions (23 total).
- Community testing is ongoing and has reported partial improvements; no complete runtime matrix is claimed. Sim approach/positioning and the exact cause of the reported Necromancer spellbook contamination remain pending reproduction; no saved learned-ability data is deleted.


## 1.9.0 — integrated upstream in PR #1

Based on upstream tag 1.8.4, commit `ff821f1d2cfce194f792c68749e6dd51491eb5ea`. Consolidates local 1.9.0 work and subsequent locally labeled 1.9.1 changes. Local build labels are not official upstream releases.

### Native Lunaris

- Replace the BepInEx base/config/logger with Lunaris registration and compatibility adapters; explicitly request Harmony permission.
- Use the full EverShor project branding as the plugin display name while retaining the existing plugin GUID and author credit.
- Expose native Skills and Extra Settings commands. Preserve legacy JSON data discovery; loader option values are not imported.
- Save data/options, stop coroutines, detach scene callbacks, unpatch Harmony, release UI and clear references during unload.
- Add configurable local reference paths and a PowerShell build entry point; do not distribute game/runtime dependencies.

### Character selection and Monk

- Put Monk and Necromancer on a shared EverShor page instead of expanding the native grid and displacing proficiency controls.
- Add optional Fate of the Fallen page interoperability without a compile-time dependency.
- Remove superseded grid reflow code; restore button states and destroy owned UI on unload.
- Honor player attack-sync opt-out for Monk handwraps; check Unity object validity during Monk teardown.
- Permit Reaver use of Grandmaster's Presence through aura eligibility mapping.

### Necromancer and Thrall

- Correct Venomous Blood's icon reference/asset and enable native auto-attack on offensive Necromancer definitions.
- Expand equipment UI/slot acceptance, including Aura and Charm, gear-derived statistics, weapon damage and attribute contribution.
- Use guarded skeleton/humanoid equipment-rig mapping for armor visuals.
- Give Thralls their own tier kit and equipment-granted abilities; remove copied player-learned abilities and check equipment on-hit procs.
- Add Bone Rake, Sentry's Rebuke, Grave Siphon, Crypt Breaker and Ossuary Verdict by tier; attack cooldown 6 s, tier-3 lifetap.
- Add direct Grave Challenge (1.25 s), slow from tier 2 (11 s), stun from tier 3 (15 s), and the tier-5 Ossuary Hammer variant.
- Add area taunt (2 s/12 m) limited to existing owner/Thrall combat engagement; exclude dead, charmed, NeverAggro and out-of-range targets.
- Reuse query buffers/target sets, with an overflow fallback for dense fights. Centralize and regression-test taunt eligibility.
- Release cloned Thrall spells on runtime reset; store fixed tier arrays once.
- Select simple spell templates without secondary/hardcoded effects and explicitly distinguish instant attacks from duration-based control effects.

### Safety and review tooling

- Log periodic-effect null failures once per Stats object/scene. Suppress only the Thrall path and propagate unrelated exceptions; this mitigates a managed failure, not all Unity/native crashes.
- Replace the chat-color fallback chain with explicit delegates for all 23 settings.
- Stop tracking upstream-generated bin/obj outputs and third-party lib references; keep local dependencies ignored.
- Add metadata/source/icon checks, 12 game-independent regression cases, CI, reference hashes and a manual game-test matrix.
- Restore the main README as a comprehensive player guide: all 25 skills, crafting, SimCraft, economy, bags, corpse recovery, custom class progressions, equipment, trainers and commands. Verify 35 Necromancer spells, 32 Monk techniques, shared skill lists and eight Ascensions per class against source; keep developer/test instructions separate.
- Refresh migration audit and PR instructions. Exclude standalone Compare/Diagnostics, private saves, SDK caches and cancelled class design.

### Validation

See `docs/VALIDATION.md` for local build results and the two existing Lunaris reference-version warnings. Native runtime, rendered UI, combat balance, persistence and cross-mod compatibility still require game validation.
