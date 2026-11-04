[CmdletBinding()]
param(
    [string]$DotnetPath = 'dotnet',
    [string]$ReferenceDirectory = '',
    [ValidateSet('Debug','Release')][string]$Configuration = 'Release'
)
$ErrorActionPreference = 'Stop'
$project = Join-Path $PSScriptRoot 'ErenshorSkills/ErenshorClassicSkills.csproj'
if (!$ReferenceDirectory) { $ReferenceDirectory = Join-Path $PSScriptRoot 'ErenshorSkills/lib' }
$ReferenceDirectory = (Resolve-Path -LiteralPath $ReferenceDirectory).Path
[xml]$xml = Get-Content -LiteralPath $project -Raw
foreach ($reference in $xml.Project.ItemGroup.Reference) {
    if (!$reference.Include) { continue }
    $path = Join-Path $ReferenceDirectory ($reference.Include + '.dll')
    if (!(Test-Path -LiteralPath $path)) {
        throw "Missing reference: $path. See docs/BUILDING.md."
    }
}
& $DotnetPath build $project -c $Configuration "-p:ReferenceDirectory=$ReferenceDirectory" -v:minimal
if ($LASTEXITCODE -ne 0) { throw 'Plugin build failed.' }
$output = Join-Path $PSScriptRoot "ErenshorSkills/bin/$Configuration/netstandard2.1/ErenshorClassicSkills.dll"
if (!(Test-Path -LiteralPath $output)) { throw 'Build produced no plugin DLL.' }
Write-Host "Build output: $output"
