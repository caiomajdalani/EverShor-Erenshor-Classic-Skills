[CmdletBinding()]
param(
    [switch]$SkipBuild,
    [string]$DotnetPath = 'dotnet',
    [string]$ReferenceDirectory = ''
)
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$mod = Join-Path $root 'ErenshorClassicSkills'
$project = Join-Path $mod 'ErenshorSkills/ErenshorClassicSkills.csproj'
[xml]$xml = Get-Content -LiteralPath $project -Raw
$version = [string]$xml.Project.PropertyGroup.Version
$manifest = Get-Content (Join-Path $mod 'manifest.json') -Raw | ConvertFrom-Json
$plugin = Get-Content (Join-Path $mod 'ErenshorSkills/Plugin.cs') -Raw
if ($manifest.version_number -ne $version -or
    $plugin -notmatch ('PluginVersion = "' + [regex]::Escape($version) + '"')) {
    throw 'Plugin, project and manifest versions disagree.'
}
Write-Host "PASS version metadata: $version"
if ($xml.Project.ItemGroup.Reference.Include -contains 'BepInEx') {
    throw 'Legacy loader reference in native build.'
}
$legacy = Get-ChildItem (Join-Path $mod 'ErenshorSkills') -Filter *.cs -Recurse |
    Where-Object FullName -NotMatch '[\\/](bin|obj)[\\/]' |
    Select-String -Pattern '^\s*using BepInEx|:\s*BaseUnityPlugin|\[BepInPlugin'
if ($legacy) { throw 'Legacy loader API usage remains in source.' }
Write-Host 'PASS native loader source scan'

$icons = Join-Path $mod 'ErenshorSkills/Resources/Icons'
$expected = [ordered]@{
    'Monk Skill *.png'=32; 'Monk Tome *.png'=32
    'Monk Aura Item *.png'=5; 'Monk Aura Spell *.png'=5
    'Monk Gear *.png'=27; 'Monk Class.png'=1
    'Necromancer Spell *.png'=37; 'Necromancer Scroll *.png'=35
    'Necromancer Aura Item *.png'=5; 'Necromancer Aura Spell *.png'=5
    'Necromancer Gear *.png'=5; 'Necromancer Class.png'=1; 'Corpse Bag.png'=1
}
foreach ($pattern in $expected.Keys) {
    $files = @(Get-ChildItem -LiteralPath $icons -Filter $pattern -File |
        Where-Object Length -GT 0)
    if ($files.Count -ne $expected[$pattern]) { throw "Incomplete catalog: $pattern" }
}
$hashes = foreach ($png in Get-ChildItem -LiteralPath $icons -Filter *.png -File) {
    $bytes = [IO.File]::ReadAllBytes($png.FullName)
    if ($bytes.Length -lt 24 -or
        [BitConverter]::ToString($bytes, 0, 8) -ne '89-50-4E-47-0D-0A-1A-0A') {
        throw "Invalid PNG header: $($png.Name)"
    }
    (Get-FileHash -LiteralPath $png.FullName -Algorithm SHA256).Hash
}
if (@($hashes | Group-Object | Where-Object Count -GT 1).Count) {
    throw 'Exact duplicate icon images detected.'
}
Write-Host "PASS icon catalogs, PNG headers and uniqueness ($($hashes.Count) icons)"

& $DotnetPath run --project (Join-Path $root 'tests/CombatPolicy.Tests.csproj') -c Release
if ($LASTEXITCODE -ne 0) { throw 'Combat policy regression tests failed.' }
if (!$SkipBuild) {
    & (Join-Path $mod 'build.ps1') -DotnetPath $DotnetPath -ReferenceDirectory $ReferenceDirectory
    & (Join-Path $PSScriptRoot 'Audit-HarmonyTargets.ps1') -ReferenceDirectory $ReferenceDirectory
}
Write-Host 'Validation completed. In-game checks are documented separately in docs/VALIDATION.md.'
