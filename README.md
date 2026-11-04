# EverShor - Classic Skills, Classes and Features

An EverQuest-inspired Erenshor mod by **KrautchanPro**: skills, tradeskills, custom items, SimCraft, Monk and Necromancer classes.

This branch proposes **1.9.0** on upstream **1.8.4** (`ff821f1d2cfce194f792c68749e6dd51491eb5ea`). It includes native Lunaris migration, character selection, Thrall equipment/combat and lifecycle changes. It is a review candidate, not an in-game-certified release.

## Installation and migration

1. Install Lunaris for the supported public Erenshor build.
2. Back up character saves and ClassicSkills data before migrating.
3. Exit the game for the initial migration and remove the previous Classic Skills plugin from its old loader directory.
4. Put one `ErenshorClassicSkills.dll` in `<Erenshor>/plugins/`. Enable **EverShor - Classic Skills, Classes and Features** before loading a character so custom item IDs resolve.
5. Review the native Options and restart once after initial enablement.

This build requires Lunaris and no longer loads directly through BepInEx. Existing JSON under the legacy ClassicSkills data directory remains discoverable; BepInEx option values are not imported. Do not install duplicate Classic Skills DLLs or the standalone Attack Sync Fix. Backward compatibility of new Thrall equipment with older releases has not been certified.

## Features

- Utility, weapon, magic and tradeskill progression, resource gathering and crafting.
- SimCraft and simulated player skill bonuses.
- Playable Monk and Necromancer on a shared EverShor class page.
- Thrall equipment slot validation, weapon/stat contributions, Aura/Charm support and persistent gear.
- Tier-specific Thrall attacks, threat and control abilities; equipment-granted abilities instead of a copy of the player's learned spellbook.
- Native Lunaris Options and commands: `/erenshorclassicskills skills` and `/erenshorclassicskills extrasettings`.

Open Skills with **F8** by default. See the [detailed feature/configuration reference](ErenshorClassicSkills/README.md).

## Build and validation

Use .NET SDK 8 and PowerShell 7. Supply game/Unity references, `Lunaris.dll` and `0Harmony.dll` locally; these DLLs are not distributed in source control. See [BUILDING.md](docs/BUILDING.md).

```powershell
./ErenshorClassicSkills/build.ps1
./scripts/Validate.ps1
# Reference-free CI subset:
./scripts/Validate.ps1 -SkipBuild
```

The plugin targets netstandard2.1. Output: `ErenshorClassicSkills/ErenshorSkills/bin/Release/netstandard2.1/ErenshorClassicSkills.dll`. Install only that plugin DLL. See [validation results and pending game checks](docs/VALIDATION.md).

## Compatibility and scope

The inherited migration identifies its local reference set as public Steam build `24405256`. Exact [assembly hashes](docs/reference-assemblies.json) document the build inputs. Compilation does not certify later Erenshor/Lunaris releases or the community playtest.

Fate of the Fallen interoperability is optional and uses reflection. Different mod load orders still require game testing. Item Compare and Diagnostics remain separate plugins and are not bundled here. The cancelled Death Knight concept is not included.

Read the [changelog](CHANGELOG.md), [change inventory](docs/CHANGE_INVENTORY.md), [migration audit](ErenshorClassicSkills/NATIVE-LUNARIS-MIGRATION-AUDIT.md) and [prepared PR description](docs/PR_DESCRIPTION.md).

## Attribution and licensing

Original project/author credits and third-party notices are preserved. Root `LICENSE` and nested `ErenshorClassicSkills/LICENSE.txt` retain the different license texts already present upstream; this contribution does not alter or resolve their scope. The maintainer should clarify that existing distinction before distributing a release.
