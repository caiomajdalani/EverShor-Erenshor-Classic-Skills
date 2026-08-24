#!/bin/bash
# ═══════════════════════════════════════════════════════════════
# ErenshorClassicSkills — Build & Install Script
# Run from the extracted ErenshorClassicSkills/ directory
# ═══════════════════════════════════════════════════════════════

# ── Configuration ─────────────────────────────────────────────
# ERENSHOR_DIR can be supplied explicitly. Otherwise detect common Steam
# libraries, including the secondary library used on this machine.
if [ -z "${ERENSHOR_DIR:-}" ]; then
    for candidate in \
        "/mnt/games/steamapps/common/Erenshor" \
        "$HOME/.local/share/Steam/steamapps/common/Erenshor" \
        "$HOME/.steam/steam/steamapps/common/Erenshor" \
        "$HOME/.var/app/com.valvesoftware.Steam/.local/share/Steam/steamapps/common/Erenshor"; do
        if [ -d "$candidate" ]; then
            ERENSHOR_DIR="$candidate"
            break
        fi
    done
fi

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
cd "$SCRIPT_DIR" || exit 1
MANAGED_DIR="$ERENSHOR_DIR/Erenshor_Data/Managed"
BEPINEX_DIR="$ERENSHOR_DIR/BepInEx"
BEPINEX_CORE="$BEPINEX_DIR/core"
PLUGINS_DIR="$BEPINEX_DIR/plugins"

PROJECT_DIR="ErenshorSkills"
LIB_DIR="$PROJECT_DIR/lib"
OUTPUT_DLL="$PROJECT_DIR/bin/Release/netstandard2.1/ErenshorClassicSkills.dll"

echo "═══════════════════════════════════════════"
echo "  ErenshorClassicSkills Build Script"
echo "═══════════════════════════════════════════"
echo ""

# ── Verify paths ─────────────────────────────────────────────
if [ ! -d "$ERENSHOR_DIR" ]; then
    echo "ERROR: Erenshor not found. Set ERENSHOR_DIR to the game directory."
    exit 1
fi

if [ ! -d "$BEPINEX_CORE" ]; then
    echo "ERROR: BepInEx not found at $BEPINEX_DIR"
    echo "Install BepInEx first:"
    echo "  https://thunderstore.io/c/erenshor/p/BepInEx/BepInExPack/"
    exit 1
fi

echo "✓ Erenshor: $ERENSHOR_DIR"
echo "✓ BepInEx:  $BEPINEX_DIR"
echo ""

# ── Copy DLLs to lib/ ────────────────────────────────────────
echo "Copying DLLs to lib/ ..."
mkdir -p "$LIB_DIR"

cp "$BEPINEX_CORE/BepInEx.dll"  "$LIB_DIR/" 2>/dev/null && echo "  ✓ BepInEx.dll"  || echo "  ✗ BepInEx.dll"
cp "$BEPINEX_CORE/0Harmony.dll" "$LIB_DIR/" 2>/dev/null && echo "  ✓ 0Harmony.dll" || echo "  ✗ 0Harmony.dll"

for dll in Assembly-CSharp.dll UnityEngine.dll UnityEngine.CoreModule.dll \
           UnityEngine.IMGUIModule.dll UnityEngine.UIModule.dll UnityEngine.InputLegacyModule.dll \
           UnityEngine.PhysicsModule.dll UnityEngine.TextRenderingModule.dll UnityEngine.JSONSerializeModule.dll \
           UnityEngine.ImageConversionModule.dll \
           UnityEngine.UI.dll \
           UnityEngine.AudioModule.dll \
           UnityEngine.AnimationModule.dll \
           UnityEngine.ParticleSystemModule.dll \
           UnityEngine.ScreenCaptureModule.dll \
           UnityEngine.AIModule.dll \
           Unity.TextMeshPro.dll; do
    cp "$MANAGED_DIR/$dll" "$LIB_DIR/" 2>/dev/null && echo "  ✓ $dll" || echo "  ✗ $dll"
done
echo ""

# ── Sync embedded resources (icons + models) ─────────────────
ICON_SRC="$PLUGINS_DIR/ClassicSkills/Icons"
MODEL_SRC="$PLUGINS_DIR/ClassicSkills/Models"
RES_ICONS="$PROJECT_DIR/Resources/Icons"
RES_MODELS="$PROJECT_DIR/Resources/Models"

mkdir -p "$RES_ICONS" "$RES_MODELS"

if [ -d "$ICON_SRC" ]; then
    # Merge user-overridable disk icons into the embedded source catalog.
    # Do not delete source-only class assets: Monk/Necromancer spell, tome,
    # aura, gear, and class icons are intentionally embedded even when the
    # external override folder does not contain copies of them.
    rsync -a "$ICON_SRC/" "$RES_ICONS/" 2>/dev/null || \
        cp -u "$ICON_SRC/"*.png "$RES_ICONS/" 2>/dev/null
    echo "✓ Synced $(ls "$RES_ICONS/" | wc -l) icons into DLL resources"
fi
if [ -d "$MODEL_SRC" ]; then
    rsync -a --delete "$MODEL_SRC/" "$RES_MODELS/" 2>/dev/null || \
        cp -u "$MODEL_SRC/"* "$RES_MODELS/" 2>/dev/null
    echo "✓ Synced $(ls "$RES_MODELS/" | wc -l) model files into DLL resources"
fi
echo ""

# Refuse to publish a build that would render custom class content as white
# tiles or silently fall back to repeated native spell icons.
require_icon_count() {
    pattern="$1"
    expected="$2"
    label="$3"
    actual="$(find "$RES_ICONS" -maxdepth 1 -type f -name "$pattern" \
        -size +0c | wc -l)"
    if [ "$actual" -ne "$expected" ]; then
        echo "ERROR: $label icon catalog is incomplete ($actual/$expected)."
        return 1
    fi
    echo "  ✓ $label icons: $actual/$expected"
}

echo "Validating required custom class icons ..."
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
ICON_DUPLICATE_HASHES="$(find "$RES_ICONS" -maxdepth 1 -type f \
    -name "*.png" -print0 | xargs -0 sha256sum | cut -d' ' -f1 | \
    sort | uniq -d | wc -l)"
if [ "$ICON_DUPLICATE_HASHES" -ne 0 ]; then
    echo "ERROR: embedded icon catalog contains $ICON_DUPLICATE_HASHES exact duplicate image hash(es)."
    ICON_VALID=1
else
    echo "  ✓ Embedded icon PNG content is exact-hash unique"
fi
if [ "$ICON_VALID" -ne 0 ]; then
    echo "BUILD ABORTED: restore or regenerate the missing unique icons."
    exit 1
fi
echo ""

# ── Build ─────────────────────────────────────────────────────
echo "Building..."
cd "$PROJECT_DIR" || exit 1
dotnet build -c Release 2>&1
BUILD_RESULT=$?
cd ..

if [ $BUILD_RESULT -ne 0 ]; then
    echo ""
    echo "BUILD FAILED."
    echo "  - Check that all DLLs copied to lib/"
    echo "  - Install .NET SDK: sudo pacman -S dotnet-sdk"
    exit 1
fi

echo ""
echo "✓ Build successful"

# ── Install ──────────────────────────────────────────────────
echo "Installing to plugins/ ..."
if pgrep -f '[E]renshor\.exe' >/dev/null; then
    echo "ERROR: Erenshor is running. Exit the game before installing a DLL."
    exit 1
fi
mkdir -p "$PLUGINS_DIR"
INSTALL_TARGET="$PLUGINS_DIR/ErenshorClassicSkills.dll"
INSTALL_TEMP="$PLUGINS_DIR/.ErenshorClassicSkills.dll.tmp.$$"
if [ -f "$INSTALL_TARGET" ]; then
    INSTALL_BACKUP="$INSTALL_TARGET.backup-$(date -u +%Y%m%dT%H%M%SZ)"
    cp -p "$INSTALL_TARGET" "$INSTALL_BACKUP" || {
        echo "✗ Could not back up the installed DLL"; exit 1;
    }
    echo "  ✓ Backup: $INSTALL_BACKUP"
fi
cp "$OUTPUT_DLL" "$INSTALL_TEMP" &&
    mv -f "$INSTALL_TEMP" "$INSTALL_TARGET" &&
    echo "✓ Installed atomically to $PLUGINS_DIR" || {
        rm -f "$INSTALL_TEMP"
        echo "✗ Atomic install failed"
        exit 1
    }

echo "✓ Icons and models are embedded in the DLL ($(du -sh "$OUTPUT_DLL" | cut -f1))"

echo ""
echo "═══════════════════════════════════════════"
echo "  DONE — Launch Erenshor through Steam"
echo ""
echo "  F8  Skills window     /skills  Chat overview"
echo "  F9  Forage            /smith   Smithing"
echo "  F10 Bind Wound        /bake    Baking"
echo "  ;   Beg               /brew    Brewing"
echo "  Sit Meditate (auto)   /fletch  Fletching"
echo "                        /jewel   Jewelcraft"
echo "                        /tailor  Tailoring"
echo ""
echo "  SimCraft: /whisper SimName craft 5 Copper Rivets"
echo "═══════════════════════════════════════════"
