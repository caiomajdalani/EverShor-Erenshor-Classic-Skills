# Validation record — 2026-11-04

Baseline: upstream 1.8.4 / `ff821f1d2cfce194f792c68749e6dd51491eb5ea`. Candidate: proposed 1.9.0 on branch `modernization/native-lunaris-1.9.0`. Local 1.9.1 numbering was consolidated into this proposal; it does not overwrite an official release.

## Automated evidence

| Check | Result | Scope |
|---|---|---|
| Release / Debug builds | PASS, zero errors | .NET SDK 8.0.427, supplied assembly hashes |
| Compiler warnings | 2 MSB3277 warnings per build | Lunaris System.Net.Http/System.IO.Compression versions; see BUILDING.md |
| Version agreement | PASS | project, plugin constant and manifest all 1.9.0 |
| Native loader source scan | PASS | legacy API declarations/references absent; migration data-path strings allowed |
| Icon validation | PASS | 644 PNG headers, required class catalog counts, no exact duplicate hashes |
| Combat policy regressions | PASS, 12 cases | actual production file linked into dependency-free runner |
| Literal Harmony target audit | PASS, 86 targets | typeof + string method-name attributes resolve in supplied assemblies |
| Bash script | NOT RUN | No Bash executable available locally; use the validated PowerShell entry point |

The target audit does not resolve every overload, nameof-based annotation, optional reflection bridge or patch order. PNG header/hash checks do not prove rendering quality. No performance timing or profiler session was performed: allocation reductions are established by source inspection, not a measured FPS claim.

## Review fixes

- Reuse collider/target buffers for area taunt; use an allocating fallback only when the fixed buffer fills. Check distance by NPC position, preventing oversized colliders outside the 12 m center radius from qualifying.
- Share cooldown/radius/eligibility constants with regression tests. Cover neutral/dead/charmed/NeverAggro targets, boundary/outside radius, invalid distances and requested cooldown limits.
- Dispose cached private spell clones on reset. Reject spell templates with secondary/hardcoded behavior; explicitly set instant versus duration-effect semantics for attacks, slow and stun.
- Remove dead class-grid reflow routines. Release owned pager UI and stale manager state.
- Replace implicit chat-color fallback selection with explicit delegates.
- Scope periodic-effect exception suppression to owned Thralls and reset deduplication per scene/unload. Preserve exceptions originating outside this subsystem.
- Remove repeated equipment membership scans from an ability list already assembled exclusively from the innate kit and equipped items.

## Pending game matrix

All items below are **NOT RUN in this audit**. Earlier anecdotal reports are not counted as reproducible certification.

- [ ] Fresh Lunaris install: Options/commands, initial character creation and load.
- [ ] Existing 1.8.4 saves and JSON fallback discovery; inspect custom items and inventory before/after migration on backups.
- [ ] Monk and Necromancer class pages at supported resolutions/UI scaling; proficiency controls remain clickable.
- [ ] Fate of the Fallen absent/present, both load orders and page transitions.
- [ ] Monk handwraps with attack sync enabled/disabled; vendors, right-click interaction, looting and combat exit.
- [ ] Necromancer offensive spell auto-attack; beneficial casts do not provoke neutral NPCs.
- [ ] All five Thrall tiers: intended ability lists, mana/cooldown behavior, aggro and actual damage; slow/stun application, duration and immunity.
- [ ] Area taunt with multiple hostile mobs, distant oversized colliders, neutral vendors, charmed NPCs, other tanks and >64 colliders.
- [ ] All equipment slots/quality levels, two bracers/rings, duplicate-slot rejection, two-handed/offhand rules, Aura/Charm and weapon/armor procs.
- [ ] Skeleton armor/weapon visuals, no stretched meshes, bows and projectile attribution.
- [ ] Equipment persistence: zone, death, dismissal, resummon, logout/restart and full-inventory return; no lost/duplicated items.
- [ ] Plugin unload/reload and game shutdown; inspect logs and Unity crash reports.
- [ ] Loot Manager chat output and Arcane UI coexistence. This PR does not independently certify or replace their configuration logic.
- [ ] DPS/tank balance in party and solo play; rapid/area taunts are gameplay changes requiring maintainer review.

## Existing limitations

This audit cannot claim all Unity crashes resolved. The scoped managed exception guard does not catch native renderer/driver failures. Loader dependency warnings and upstream's existing root/nested license distinction are documented for maintainer review. Rollback of saves written with expanded Thrall equipment remains unverified.
