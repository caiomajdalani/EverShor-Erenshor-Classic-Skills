#!/usr/bin/env bash
set -euo pipefail

# Erenshor Classic Skills — native Lunaris build
#
# Default: compile only, using the locally supplied public-game and Lunaris references.
# Optional install: INSTALL=1 ERENSHOR_DIR="/path/to/Erenshor" ./build.sh

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
PROJECT_DIR="$SCRIPT_DIR/ErenshorSkills"
LIB_DIR="$PROJECT_DIR/lib"
OUTPUT_DLL="$PROJECT_DIR/bin/Release/netstandard2.1/ErenshorClassicSkills.dll"
INSTALL_MODE="${INSTALL:-0}"
ERENSHOR_DIR="${ERENSHOR_DIR:-}"

if ! command -v dotnet >/dev/null 2>&1; then
    echo "ERROR: .NET SDK (dotnet) is required to compile the plugin."
    exit 1
fi

# The reference assemblies in lib/ are pinned to the public Erenshor build
# used by this source release. Do not refresh them automatically from an
# arbitrary local install: that could silently retarget a playtest branch.
required=(
    "Lunaris.dll"
    "0Harmony.dll"
    "Assembly-CSharp.dll"
    "UnityEngine.dll"
    "UnityEngine.CoreModule.dll"
    "UnityEngine.IMGUIModule.dll"
    "UnityEngine.InputLegacyModule.dll"
    "UnityEngine.PhysicsModule.dll"
    "UnityEngine.ParticleSystemModule.dll"
    "UnityEngine.AIModule.dll"
    "UnityEngine.JSONSerializeModule.dll"
    "UnityEngine.ImageConversionModule.dll"
    "UnityEngine.UI.dll"
    "UnityEngine.UIModule.dll"
    "UnityEngine.TextRenderingModule.dll"
    "Unity.TextMeshPro.dll"
    "UnityEngine.AudioModule.dll"
    "UnityEngine.AnimationModule.dll"
    "UnityEngine.ScreenCaptureModule.dll"
)

for dll in "${required[@]}"; do
    if [[ ! -s "$LIB_DIR/$dll" ]]; then
        echo "ERROR: Required reference is missing: $LIB_DIR/$dll"
        exit 1
    fi
done

# Guard the target game references so the public release target is explicit.
if [[ -f "$LIB_DIR/Assembly-CSharp.dll" ]]; then
    echo "✓ Using the checked-in public Erenshor game references"
fi
echo "✓ Using the local Lunaris native API reference"
echo "✓ BepInEx is not a compile-time or runtime dependency"

# Keep packaged icon/model resources authoritative. The original release's
# catalog is already present in Resources and is embedded into the plugin DLL.
RES_ICONS="$PROJECT_DIR/Resources/Icons"
RES_MODELS="$PROJECT_DIR/Resources/Models"
mkdir -p "$RES_ICONS" "$RES_MODELS"

require_icon_count() {
    local pattern="$1" expected="$2" label="$3"
    local actual
    actual="$(find "$RES_ICONS" -maxdepth 1 -type f -name "$pattern" -size +0c | wc -l | tr -d ' ')"
    if [[ "$actual" -ne "$expected" ]]; then
        echo "ERROR: $label icon catalog is incomplete ($actual/$expected)."
        return 1
    fi
    echo "  ✓ $label icons: $actual/$expected"
}

echo "Validating custom class icon catalogs ..."
ICON_VALID=0
require_icon_count "Monk Skill *.png" 32 "Monk skill" || ICON_VALID=1
require_icon_count "Monk Tome *.png" 32 "Monk tome" || ICON_VALID=1
require_icon_count "Monk Aura Item *.png" 5 "Monk aura item" || ICON_VALID=1
require_icon_count "Monk Aura Spell *.png" 5 "Monk aura effect" || ICON_VALID=1
require_icon_count "Monk Gear *.png" 27 "Monk gear" || ICON_VALID=1
require_icon_count "Monk Class.png" 1 "Monk class" || ICON_VALID=1
require_icon_count "Necromancer Spell *.png" 37 "Necromancer spell" || ICON_VALID=1
require_icon_count "Necromancer Scroll *.png" 35 "Necromancer scroll" || ICON_VALID=1
require_icon_count "Necromancer Aura Item *.png" 5 "Necromancer aura item" || ICON_VALID=1
require_icon_count "Necromancer Aura Spell *.png" 5 "Necromancer aura effect" || ICON_VALID=1
require_icon_count "Necromancer Gear *.png" 5 "Necromancer gear" || ICON_VALID=1
require_icon_count "Necromancer Class.png" 1 "Necromancer class" || ICON_VALID=1
require_icon_count "Corpse Bag.png" 1 "Corpse Bag" || ICON_VALID=1
DUPLICATES="$(find "$RES_ICONS" -maxdepth 1 -type f -name '*.png' -print0 | xargs -0 -r sha256sum | cut -d' ' -f1 | sort | uniq -d | wc -l | tr -d ' ')"
if [[ "$DUPLICATES" -ne 0 ]]; then
    echo "ERROR: icon catalog contains $DUPLICATES duplicate image hash(es)."
    ICON_VALID=1
else
    echo "  ✓ PNG catalog has no exact duplicate image hashes"
fi
if [[ "$ICON_VALID" -ne 0 ]]; then
    echo "BUILD ABORTED: restore the missing/duplicate icon assets."
    exit 1
fi

printf '
Building native Lunaris plugin...
'
dotnet build "$PROJECT_DIR/ErenshorClassicSkills.csproj" -c Release

if [[ ! -s "$OUTPUT_DLL" ]]; then
    echo "ERROR: build completed without the expected DLL: $OUTPUT_DLL"
    exit 1
fi

echo "✓ Build successful: $OUTPUT_DLL ($(du -h "$OUTPUT_DLL" | cut -f1))"

if [[ "$INSTALL_MODE" != "1" ]]; then
    echo "Build-only mode. To install: INSTALL=1 ERENSHOR_DIR="/path/to/Erenshor" ./build.sh"
    exit 0
fi

if [[ -z "$ERENSHOR_DIR" || ! -d "$ERENSHOR_DIR" ]]; then
    echo "ERROR: set ERENSHOR_DIR to the Erenshor game directory when INSTALL=1."
    exit 1
fi
if pgrep -f '[E]renshor\.exe' >/dev/null 2>&1; then
    echo "ERROR: Erenshor is running. Exit the game before replacing the plugin DLL."
    exit 1
fi

GAME_ASSEMBLY="$ERENSHOR_DIR/Erenshor_Data/Managed/Assembly-CSharp.dll"
if [[ "${ALLOW_GAME_ASSEMBLY_MISMATCH:-0}" != "1" ]]; then
    if [[ ! -s "$GAME_ASSEMBLY" ]]; then
        echo "ERROR: Could not find the installed game's Assembly-CSharp.dll: $GAME_ASSEMBLY"
        echo "The installer requires the public game build matching the local references."
        exit 1
    fi
    if ! cmp -s "$LIB_DIR/Assembly-CSharp.dll" "$GAME_ASSEMBLY"; then
        echo "ERROR: Installed Erenshor Assembly-CSharp.dll differs from this build's pinned public-game reference."
        echo "This package intentionally does not target the 0.8 community playtest or unknown game builds."
        echo "Re-check the official public branch before updating the reference assemblies."
        echo "Set ALLOW_GAME_ASSEMBLY_MISMATCH=1 only if you have independently verified compatibility."
        exit 1
    fi
    echo "✓ Installed game Assembly-CSharp.dll matches the pinned public build reference"
else
    echo "WARNING: bypassing the pinned game assembly check by explicit request"
fi

PLUGINS_DIR="$ERENSHOR_DIR/plugins"
mkdir -p "$PLUGINS_DIR"
INSTALL_TARGET="$PLUGINS_DIR/ErenshorClassicSkills.dll"
INSTALL_TEMP="$PLUGINS_DIR/.ErenshorClassicSkills.dll.tmp.$$"
if [[ -f "$INSTALL_TARGET" ]]; then
    BACKUP="$INSTALL_TARGET.backup-$(date -u +%Y%m%dT%H%M%SZ)"
    cp -p "$INSTALL_TARGET" "$BACKUP"
    echo "✓ Existing plugin backed up to: $BACKUP"
fi
cp "$OUTPUT_DLL" "$INSTALL_TEMP"
mv -f "$INSTALL_TEMP" "$INSTALL_TARGET"
echo "✓ Installed native Lunaris plugin to: $INSTALL_TARGET"
echo "Do not leave a second copy under BepInEx/plugins."
