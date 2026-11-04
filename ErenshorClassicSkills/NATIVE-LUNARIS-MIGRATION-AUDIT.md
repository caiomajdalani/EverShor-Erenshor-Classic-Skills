# Native Lunaris migration audit — proposed 1.9.0

Audit date: 2026-11-04. Baseline: upstream tag 1.8.4, commit ff821f1d2cfce194f792c68749e6dd51491eb5ea.

## Loader boundary

SkillsPlugin now derives from LunarisPlugin, registers ClassicSkillsSettings through Config.Register, uses Lunaris plugin/command/permission metadata and adapts ILog at existing call sites. The internal ConfigEntry adapter does not depend on BepInEx. Legacy directory names are retained only for JSON data discovery; legacy option values are not imported.

## Lifecycle

Unload stops work, saves options/data, releases UI, detaches scene events and unpatches Harmony. Settings/logger/path adapters are cleared. The review additionally releases private Thrall spell clones, clears crash-log deduplication state and restores/destroys owned class-selection buttons. Full unload/reload and shutdown behavior still need native runtime testing; no universal crash fix is claimed.

## Additional gameplay scope

This proposal also contains Thrall equipment, damage, ability-kit and threat/control changes, plus Monk attack-sync opt-out and a shared character-creation pager. It is not a loader-only or behavior-neutral refactor. The changelog and file inventory describe these changes for separate balance review.

## Build and validation

Release compilation succeeds with local game/Unity/Lunaris/Harmony references. No BepInEx API declarations remain in source, and no third-party reference DLLs are staged for the PR. Twelve game-independent combat-policy regression checks and icon/metadata checks pass. Two MSB3277 dependency-version warnings remain. See ../docs/VALIDATION.md and ../docs/reference-assemblies.json for the reproducible record and pending game matrix.

The preexisting nested MIT and root GPL license texts are preserved without reinterpretation. References to a pinned Steam build describe the inherited local reference set, not an independently verified current Steam release.
