# Change inventory against upstream 1.8.4

Baseline: ff821f1d2cfce194f792c68749e6dd51491eb5ea. Every changed/added/removed path is listed below. Behavioral detail and validation limits are in CHANGELOG.md and VALIDATION.md. Generated/reference files are removed from source control, not deleted from the developer machine.

| Path | Review purpose |
|---|---|
| `.github/workflows/validate.yml` | Automated regression, metadata/assets/target checks and reference-free CI |
| `.gitignore` | Ignore local dependencies, generated files and build artifacts |
| `CHANGELOG.md` | Release/migration/build/validation documentation or version metadata |
| `docs/BUILDING.md` | Release/migration/build/validation documentation or version metadata |
| `docs/CHANGE_INVENTORY.md` | Release/migration/build/validation documentation or version metadata |
| `docs/PR_DESCRIPTION.md` | Release/migration/build/validation documentation or version metadata |
| `docs/reference-assemblies.json` | Release/migration/build/validation documentation or version metadata |
| `docs/SUBMIT_PR.md` | Release/migration/build/validation documentation or version metadata |
| `docs/VALIDATION.md` | Release/migration/build/validation documentation or version metadata |
| `ErenshorClassicSkills/.gitignore` | Ignore local dependencies, generated files and build artifacts |
| `ErenshorClassicSkills/build.ps1` | Native loader build references, configurable local inputs and build entry points |
| `ErenshorClassicSkills/build.sh` | Native loader build references, configurable local inputs and build entry points |
| `ErenshorClassicSkills/ErenshorClassicSkills-v1.9.0-Changes.md` | Release/migration/build/validation documentation or version metadata |
| `ErenshorClassicSkills/ErenshorSkills/bin/Debug/netstandard2.1/ErenshorClassicSkills.deps.json` | Remove tracked generated build output/cache |
| `ErenshorClassicSkills/ErenshorSkills/bin/Debug/netstandard2.1/ErenshorClassicSkills.dll` | Remove tracked generated build output/cache |
| `ErenshorClassicSkills/ErenshorSkills/bin/Debug/netstandard2.1/ErenshorClassicSkills.pdb` | Remove tracked generated build output/cache |
| `ErenshorClassicSkills/ErenshorSkills/bin/Debug/netstandard2.1/UnityEngine.ImageConversionModule.dll` | Remove tracked generated build output/cache |
| `ErenshorClassicSkills/ErenshorSkills/bin/Debug/netstandard2.1/UnityEngine.JSONSerializeModule.dll` | Remove tracked generated build output/cache |
| `ErenshorClassicSkills/ErenshorSkills/ClassicSkillsSettings.cs` | Native Lunaris settings/logging/paths and lifecycle integration |
| `ErenshorClassicSkills/ErenshorSkills/ErenshorClassicSkills.csproj` | Native loader build references, configurable local inputs and build entry points |
| `ErenshorClassicSkills/ErenshorSkills/Features/LogDisplayOptions.cs` | Native chat settings and explicit color delegates |
| `ErenshorClassicSkills/ErenshorSkills/Items/BagSystem.cs` | Character-style Thrall equipment UI and slot acceptance |
| `ErenshorClassicSkills/ErenshorSkills/Items/ItemFactory.cs` | Native compatibility integration and offensive spell auto-attack support |
| `ErenshorClassicSkills/ErenshorSkills/lib/0Harmony.dll` | Remove tracked third-party compile/runtime reference |
| `ErenshorClassicSkills/ErenshorSkills/lib/Assembly-CSharp.dll` | Remove tracked third-party compile/runtime reference |
| `ErenshorClassicSkills/ErenshorSkills/lib/BepInEx.dll` | Remove tracked third-party compile/runtime reference |
| `ErenshorClassicSkills/ErenshorSkills/lib/Unity.TextMeshPro.dll` | Remove tracked third-party compile/runtime reference |
| `ErenshorClassicSkills/ErenshorSkills/lib/UnityEngine.AIModule.dll` | Remove tracked third-party compile/runtime reference |
| `ErenshorClassicSkills/ErenshorSkills/lib/UnityEngine.AnimationModule.dll` | Remove tracked third-party compile/runtime reference |
| `ErenshorClassicSkills/ErenshorSkills/lib/UnityEngine.AudioModule.dll` | Remove tracked third-party compile/runtime reference |
| `ErenshorClassicSkills/ErenshorSkills/lib/UnityEngine.CoreModule.dll` | Remove tracked third-party compile/runtime reference |
| `ErenshorClassicSkills/ErenshorSkills/lib/UnityEngine.dll` | Remove tracked third-party compile/runtime reference |
| `ErenshorClassicSkills/ErenshorSkills/lib/UnityEngine.ImageConversionModule.dll` | Remove tracked third-party compile/runtime reference |
| `ErenshorClassicSkills/ErenshorSkills/lib/UnityEngine.IMGUIModule.dll` | Remove tracked third-party compile/runtime reference |
| `ErenshorClassicSkills/ErenshorSkills/lib/UnityEngine.InputLegacyModule.dll` | Remove tracked third-party compile/runtime reference |
| `ErenshorClassicSkills/ErenshorSkills/lib/UnityEngine.JSONSerializeModule.dll` | Remove tracked third-party compile/runtime reference |
| `ErenshorClassicSkills/ErenshorSkills/lib/UnityEngine.ParticleSystemModule.dll` | Remove tracked third-party compile/runtime reference |
| `ErenshorClassicSkills/ErenshorSkills/lib/UnityEngine.PhysicsModule.dll` | Remove tracked third-party compile/runtime reference |
| `ErenshorClassicSkills/ErenshorSkills/lib/UnityEngine.ScreenCaptureModule.dll` | Remove tracked third-party compile/runtime reference |
| `ErenshorClassicSkills/ErenshorSkills/lib/UnityEngine.TextRenderingModule.dll` | Remove tracked third-party compile/runtime reference |
| `ErenshorClassicSkills/ErenshorSkills/lib/UnityEngine.UI.dll` | Remove tracked third-party compile/runtime reference |
| `ErenshorClassicSkills/ErenshorSkills/lib/UnityEngine.UIModule.dll` | Remove tracked third-party compile/runtime reference |
| `ErenshorClassicSkills/ErenshorSkills/Monk/MonkAuras.cs` | Reaver eligibility for martial aura |
| `ErenshorClassicSkills/ErenshorSkills/Monk/MonkPatches.cs` | Shared class pager integration, obsolete reflow removal; pet hooks in Necromancer patches |
| `ErenshorClassicSkills/ErenshorSkills/Monk/MonkRuntime.cs` | Native attack-sync options, handwrap opt-out and teardown validity |
| `ErenshorClassicSkills/ErenshorSkills/NativeConfigEntry.cs` | Native Lunaris settings/logging/paths and lifecycle integration |
| `ErenshorClassicSkills/ErenshorSkills/Necromancer/NecromancerCatalog.cs` | Spell binding/cast eligibility, icon/auto-attack definitions and kit cleanup |
| `ErenshorClassicSkills/ErenshorSkills/Necromancer/NecromancerPatches.cs` | Shared class pager integration, obsolete reflow removal; pet hooks in Necromancer patches |
| `ErenshorClassicSkills/ErenshorSkills/Necromancer/NecromancerPetEquipment.cs` | Equipped slot classification and stat/item handling |
| `ErenshorClassicSkills/ErenshorSkills/Necromancer/NecromancerPets.cs` | Gear-derived combat values, visuals, ability assignment and bounded area taunt |
| `ErenshorClassicSkills/ErenshorSkills/Necromancer/NecromancerRuntime.cs` | Spell binding/cast eligibility, icon/auto-attack definitions and kit cleanup |
| `ErenshorClassicSkills/ErenshorSkills/Necromancer/NecromancerSpellData.cs` | Spell binding/cast eligibility, icon/auto-attack definitions and kit cleanup |
| `ErenshorClassicSkills/ErenshorSkills/Necromancer/NecromancerThrallAbilities.cs` | Private tier ability kit/cache lifecycle and regression-testable taunt policy |
| `ErenshorClassicSkills/ErenshorSkills/Necromancer/ThrallCombatPolicy.cs` | Private tier ability kit/cache lifecycle and regression-testable taunt policy |
| `ErenshorClassicSkills/ErenshorSkills/obj/Debug/netstandard2.1/Erenshor.CD7C20DC.Up2Date` | Remove tracked generated build output/cache |
| `ErenshorClassicSkills/ErenshorSkills/obj/Debug/netstandard2.1/ErenshorClassicSkills.AssemblyInfo.cs` | Remove tracked generated build output/cache |
| `ErenshorClassicSkills/ErenshorSkills/obj/Debug/netstandard2.1/ErenshorClassicSkills.AssemblyInfoInputs.cache` | Remove tracked generated build output/cache |
| `ErenshorClassicSkills/ErenshorSkills/obj/Debug/netstandard2.1/ErenshorClassicSkills.assets.cache` | Remove tracked generated build output/cache |
| `ErenshorClassicSkills/ErenshorSkills/obj/Debug/netstandard2.1/ErenshorClassicSkills.csproj.AssemblyReference.cache` | Remove tracked generated build output/cache |
| `ErenshorClassicSkills/ErenshorSkills/obj/Debug/netstandard2.1/ErenshorClassicSkills.csproj.CoreCompileInputs.cache` | Remove tracked generated build output/cache |
| `ErenshorClassicSkills/ErenshorSkills/obj/Debug/netstandard2.1/ErenshorClassicSkills.csproj.FileListAbsolute.txt` | Remove tracked generated build output/cache |
| `ErenshorClassicSkills/ErenshorSkills/obj/Debug/netstandard2.1/ErenshorClassicSkills.dll` | Remove tracked generated build output/cache |
| `ErenshorClassicSkills/ErenshorSkills/obj/Debug/netstandard2.1/ErenshorClassicSkills.GeneratedMSBuildEditorConfig.editorconfig` | Remove tracked generated build output/cache |
| `ErenshorClassicSkills/ErenshorSkills/obj/Debug/netstandard2.1/ErenshorClassicSkills.pdb` | Remove tracked generated build output/cache |
| `ErenshorClassicSkills/ErenshorSkills/obj/ErenshorClassicSkills.csproj.nuget.dgspec.json` | Remove tracked generated build output/cache |
| `ErenshorClassicSkills/ErenshorSkills/obj/ErenshorClassicSkills.csproj.nuget.g.props` | Remove tracked generated build output/cache |
| `ErenshorClassicSkills/ErenshorSkills/obj/ErenshorClassicSkills.csproj.nuget.g.targets` | Remove tracked generated build output/cache |
| `ErenshorClassicSkills/ErenshorSkills/obj/project.assets.json` | Remove tracked generated build output/cache |
| `ErenshorClassicSkills/ErenshorSkills/obj/project.nuget.cache` | Remove tracked generated build output/cache |
| `ErenshorClassicSkills/ErenshorSkills/Patches/AllPatches.cs` | Native compatibility integration and offensive spell auto-attack support |
| `ErenshorClassicSkills/ErenshorSkills/Patches/AttackSyncFix.cs` | Native attack-sync options, handwrap opt-out and teardown validity |
| `ErenshorClassicSkills/ErenshorSkills/Patches/EverShorCharacterCreationPager.cs` | Shared class pages, owned UI cleanup and optional Fate bridge |
| `ErenshorClassicSkills/ErenshorSkills/Patches/FateOfTheFallenCharacterCreationCompatibility.cs` | Shared class pages, owned UI cleanup and optional Fate bridge |
| `ErenshorClassicSkills/ErenshorSkills/Patches/StatsTickEffectsCrashGuard.cs` | Scoped managed exception mitigation and scene-bounded diagnostics |
| `ErenshorClassicSkills/ErenshorSkills/Plugin.cs` | Native Lunaris settings/logging/paths and lifecycle integration |
| `ErenshorClassicSkills/ErenshorSkills/Resources/Icons/Necromancer Spell venomous_blood.png` | Corrected Venomous Blood icon asset |
| `ErenshorClassicSkills/ErenshorSkills/RuntimePaths.cs` | Native Lunaris settings/logging/paths and lifecycle integration |
| `ErenshorClassicSkills/ErenshorSkills/SkillData.cs` | Native Lunaris settings/logging/paths and lifecycle integration |
| `ErenshorClassicSkills/ErenshorSkills/Testing/AddonTestMode.cs` | Adapt existing in-game QA harness to native runtime paths |
| `ErenshorClassicSkills/ErenshorSkills/Testing/Level35DpsBenchmark.cs` | Adapt existing in-game QA harness to native runtime paths |
| `ErenshorClassicSkills/ErenshorSkills/UI/LogDisplaySettingsUI.cs` | Native chat settings and explicit color delegates |
| `ErenshorClassicSkills/manifest.json` | Release/migration/build/validation documentation or version metadata |
| `ErenshorClassicSkills/NATIVE-LUNARIS-MIGRATION-AUDIT.md` | Release/migration/build/validation documentation or version metadata |
| `ErenshorClassicSkills/README.md` | Release/migration/build/validation documentation or version metadata |
| `README.md` | Release/migration/build/validation documentation or version metadata |
| `scripts/Audit-HarmonyTargets.ps1` | Automated regression, metadata/assets/target checks and reference-free CI |
| `scripts/Validate.ps1` | Automated regression, metadata/assets/target checks and reference-free CI |
| `tests/CombatPolicy.Tests.csproj` | Native loader build references, configurable local inputs and build entry points |
| `tests/Program.cs` | Automated regression, metadata/assets/target checks and reference-free CI |
| `.gitattributes` | Normalize script/source line endings for reproducible checkouts |
| `docs/IN_GAME_TESTING.md` | Preserve optional QA commands outside the player-facing gameplay overview |

## 1.9.1 follow-up to merged upstream PR #1

| Path | Change |
|---|---|
| `ErenshorClassicSkills/ErenshorSkills/Monk/MonkCatalog.cs` | Null-safe lookup and authoritative live player class |
| `ErenshorClassicSkills/ErenshorSkills/Monk/MonkRuntime.cs` | Actor/target isolation, NPC dispatch and both skill-path hooks |
| `ErenshorClassicSkills/ErenshorSkills/Monk/MonkCombatPolicy.cs` | Pure activation eligibility policy |
| `tests/Program.cs`, `tests/CombatPolicy.Tests.csproj` | Eleven additional Monk regressions linked to production policy |
| `ErenshorClassicSkills/ErenshorSkills/Plugin.cs`, project and manifest | Consistent version 1.9.1 |
| `ErenshorClassicSkills/ErenshorClassicSkills-v1.9.1-Changes.md` | Follow-up release notes and unresolved runtime scenarios |
| README, changelog and review/validation documents | Full gameplay guide plus current 1.9.1 review scope |

## 1.9.2 additions to existing PR #2

- MonkRuntime.cs: actual-caster gating for Resonant Palm auto-engage, native option handling and shared caster-specific combat logging; queued follow-up eligibility.
- MonkCombatPolicy.cs and tests/Program.cs: ten additional follow-up/auto-engage regressions (33 total).
- Plugin/project/manifest: version 1.9.2.
- MONK-PUMMEL-REPORT.md, MONK-AUTOATTACK-REPORT.md and ErenshorClassicSkills-v1.9.2-Changes.md: source findings, release notes and pending runtime scenarios.
- README, changelog, PR description and validation/submission guides: consolidated 1.9.2 review scope on the existing PR branch.
