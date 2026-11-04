# Build guide

Use .NET SDK 8 (local validation: 8.0.427) and PowerShell 7. The plugin targets netstandard2.1; the dependency-free regression runner targets net8.0.

Create `ErenshorClassicSkills/ErenshorSkills/lib/` locally. Supply Lunaris.dll/0Harmony.dll from the loader and game/Unity DLLs listed in the project's Reference elements from `Erenshor_Data/Managed/`. Do not commit or distribute these third-party DLLs. `reference-assemblies.json` records the exact local hashes, not redistribution permission.

The inherited migration identifies Steam build 24405256. This audit did not independently certify Steam branches. Compare hashes and test runtime behavior before using different references.

```powershell
./ErenshorClassicSkills/build.ps1
./scripts/Validate.ps1
# Custom SDK or references:
./scripts/Validate.ps1 -DotnetPath 'C:/path/to/dotnet.exe' -ReferenceDirectory 'C:/local/references'
# Debug build:
./ErenshorClassicSkills/build.ps1 -Configuration Debug
# Reference-free CI subset:
./scripts/Validate.ps1 -SkipBuild
```

The PowerShell build checks references but does not install into a game. Install only the plugin DLL according to the root README.

The existing Bash entry point `ErenshorClassicSkills/build.sh` validates catalogs and uses the default local lib directory. Optional install uses `INSTALL=1 ERENSHOR_DIR=...` and compares the installed game assembly to the local reference. Bash, find, xargs, sha256sum, cmp and dotnet must be available.

```sh
dotnet build ErenshorClassicSkills/ErenshorSkills/ErenshorClassicSkills.csproj -c Release -p:ReferenceDirectory=/absolute/path/to/references
```

Source-only checks validate version agreement, absence of legacy loader API declarations, icon catalog counts/PNG headers/exact duplicates and the linked production taunt policy. They do not load Unity or validate rendered artwork. CI runs only this subset because game DLLs are local dependencies.

## Known warnings

The supplied Lunaris assembly references System.Net.Http and System.IO.Compression 4.2.0.0 while the netstandard2.1 reference set resolves different versions. MSBuild emits two MSB3277 warnings. They are documented, not suppressed. Do not copy additional framework DLLs into plugins to silence them; validate the chosen Lunaris runtime before release.
