# Erenshor Classic Skills v1.4.1

Compatibility, reliability, and utility-HUD update for Erenshor Steam build
**24247002** (July 17, 2026).

## New in v1.4.1

### Forage, Bind Wound, and Begging action icons

- Added three compact **42x42** action icons using the addon's existing art:
  - a leaf for **Forage**;
  - bandages for **Bind Wound**;
  - a coin for **Begging**.
- Each icon shows its current configured keybind in the corner. The defaults are
  **F9**, **F10**, and **;**.
- Each icon places a dark overlay and the numeric seconds remaining directly over
  its artwork while the action is on cooldown.
- Clicking an icon uses the same validated skill path as its hotkey, including all
  normal target, range, combat, health, inventory, and cooldown checks.
- Dragging any icon moves the entire group. The position is saved and restored
  across launches.
- Disabled skills hide their corresponding icon automatically. The whole group can
  be disabled with `Interface.ShowUtilityActionButtons=false`.
- The action group appears only after a playable character has loaded and is hidden
  at menus or during unsafe scene transitions.
- Skill-window help text now follows the configured keybind and mentions the
  matching action icon instead of displaying stale hard-coded controls.
- Added smoke checks for all three icon assets, 42x42 sizing, key labels, cooldown
  overlays, finite cooldown values, and HUD component construction.

### Ultrawide layout correction

- Fixed the utility group being pulled left over hotbar slots at 3440x1440.
- Cause: the first implementation clamped against a scaled parent rectangle whose
  reported width did not match the overlay canvas coordinate space.
- The group now uses the same stable anchored-coordinate behavior as the existing
  menu UI and remains draggable without the bad startup clamp.

## Compatibility

- Updated the addon for the current Erenshor item, inventory, equipment, combat,
  crafting, scene, and save APIs.
- Supports both **BepInEx 5** and **Lunaris**.
- Added loader-aware plugin, configuration, icon, and character-data paths.
- Added late scene/database initialization so the addon can safely load after
  Erenshor or be enabled at runtime by Lunaris.
- Added clean enable, disable, re-enable, and shutdown handling to prevent duplicate
  patches, UI, events, or registered data.
- Added compatibility handling for other mods that patch the main-hand attack-delay
  reset.

## Bag and inventory safety

- Reworked crafted bags to support native drag-and-drop in both directions.
- Items can be dragged from normal inventory into a crafted bag and from a crafted
  bag back into inventory.
- Bag windows remain above inventory windows while the dragged-item icon remains
  above the bag window.
- Added versioned bag persistence and migration for older character data.
- Added atomic writes, rotating backups, validation, and recovery for bag,
  inventory, recipe, and skill sidecars.
- Added an inventory corruption gate: if both the primary inventory file and its
  backup are invalid, saving is disabled instead of overwriting recoverable data.
- Added transaction journaling and recovery for moves, crafting, recipe scrolls,
  bank transfers, and simulated-player crafting.
- Added slot, quantity, item-type, quality, and stack-size validation.
- Prevented invalid transfers involving loot, vendors, banks, cursor state, or stale
  UI slots.
- Added protections against item loss and duplication during interruption, reload,
  partial failure, or shutdown.

## Crafting and item fixes

- Made ingredient removal and crafted-output creation transactional.
- Corrected crafting success/chance handling and failure rollback.
- Fixed recipe learning and recipe-scroll consumption behavior.
- Fixed simulated-player recipe and crafting state.
- Fixed repeated `School: Abjuration` lines in crafted consumable tooltips.
- Fixed the Pan-Seared Fish behavior reported by players.
- Fixed loot reroll behavior and several item-stat application issues.
- Expanded validation across all custom items and recipes.

## Armor, weapons, and visuals

- Fixed crafted armor appearing uniformly pink.
  - Cause: every material channel on tailored armor was overwritten with the same
    saturated pink tint.
  - Fix: native model colors are preserved and blended with restrained
    material/theme accents.
- Added distinct palettes for celestial, moon, star, phoenix, sun, nature,
  world-tree, metal, titan, dragon, wyrm, void, and night equipment.
- Corrected bracer, head, armor, weapon, shield, and back-slot visual mappings.
- Added validation for invalid color values and the former all-channel pink
  regression.
- Verified primary weapons appear in the primary hand and shields appear in the
  secondary hand.
- Verified equipment restoration after automated visual testing.

## Skills and gameplay

- Fixed Meditation behavior.
- Fixed Begging behavior and reward state.
- Added the Begging HUD action alongside Forage and Bind Wound.
- Hardened combat-skill hooks and cooldown handling.
- Made temporary buffs deterministic and safe to remove.
- Improved skill XP persistence and reduced unnecessary save churn with debounced
  writes.
- Corrected several community-reported stat, crafting, loot, and simulated-player
  issues.

## Controlled test mode

- Added an opt-in test mode with the `/cstest` command family.
- Test mode can find or grant any addon item, grant recipe materials, set skills,
  toggle test cooldowns, open test UI, run the full smoke suite, show status, and
  run the equipment visual harness.
- Test mode and test cooldowns default to **off** for normal players.
- The visual harness restores and saves the character's original equipment,
  health, and mana when it finishes.

## Verification

- Release build: **0 warnings, 0 errors**.
- All three v1.4.1 action buttons were exercised and visually confirmed in game.
- The Forage numeric cooldown overlay and all three configured key labels were
  visually confirmed at 3440x1440.
- The v1.4.0 full in-game regression baseline remains:
  - **1,252 passed, 0 failed**;
  - **745** addon items exercised and validated;
  - **280** recipes validated;
  - **176/176** equippable items passed the automated visual harness;
  - real crafted-bag persistence and bidirectional transfer tested without loss or
    duplication;
  - inventory recovery and fail-safe paths tested;
  - Lunaris late-enable, disable/re-enable, persistence, and duplicate-prevention
    tests passed.
- The v1.4.1 test session produced no addon exception. A test-only Steam retry was
  identified separately and did not originate in the addon.
- Original saves and Classic Skills sidecars were restored byte-for-byte after
  testing. Test mode is disabled in the restored player configuration.
- Release DLL SHA-256:
  `adf99b36ed714a6468f6b1c3a7ee99e2ac4ac7e7d976b8bc3f55a11ebd57448b`.

## Installation

- **BepInEx:** extract the BepInEx archive into the Erenshor game directory. The
  DLL installs to `BepInEx/plugins/ErenshorClassicSkills.dll`.
- **Lunaris:** extract the Lunaris archive into the Erenshor game directory. The
  DLL installs to `plugins/ErenshorClassicSkills.dll`, then enable it through
  Lunaris's plugin interface or `/lunaris plugins`.

Do not install both loader packages at the same time. Choose the package matching
the loader in use.
