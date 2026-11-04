[CmdletBinding()]
param([string]$ReferenceDirectory = '')
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
if (!$ReferenceDirectory) { $ReferenceDirectory = Join-Path $root 'ErenshorClassicSkills/ErenshorSkills/lib' }
$ReferenceDirectory = (Resolve-Path -LiteralPath $ReferenceDirectory).Path
$assemblies = foreach ($name in @('UnityEngine.CoreModule','UnityEngine.UI','Unity.TextMeshPro','Assembly-CSharp')) {
    [Reflection.Assembly]::LoadFrom((Join-Path $ReferenceDirectory ($name + '.dll')))
}
$checked = 0
$missing = [Collections.Generic.List[string]]::new()
foreach ($file in Get-ChildItem (Join-Path $root 'ErenshorClassicSkills/ErenshorSkills') -Recurse -Filter *.cs |
    Where-Object FullName -NotMatch '[\\/](bin|obj)[\\/]') {
    $source = Get-Content -LiteralPath $file.FullName -Raw
    foreach ($match in [regex]::Matches($source, '\[HarmonyPatch\(typeof\((\w+)\),\s*"([^"\r\n]+)"')) {
        $typeName = $match.Groups[1].Value
        $method = $match.Groups[2].Value
        $type = $null
        foreach ($assembly in $assemblies) {
            foreach ($prefix in @('', 'UnityEngine.', 'UnityEngine.UI.', 'TMPro.')) {
                $type = $assembly.GetType($prefix + $typeName)
                if ($type) { break }
            }
            if ($type) { break }
        }
        $checked++
        $flags = [Reflection.BindingFlags]'Public,NonPublic,Instance,Static'
        if (!$type -or !@($type.GetMethods($flags) | Where-Object Name -EQ $method).Count) {
            $missing.Add("$typeName.$method ($($file.Name))")
        }
    }
}
if ($missing.Count) { throw ("Missing literal Harmony targets: " + ($missing -join '; ')) }
Write-Host "PASS $checked literal Harmony target names in supplied references."
Write-Host 'Scope: literal typeof/name attributes only; overload resolution, nameof and reflection bridges still require runtime validation.'
