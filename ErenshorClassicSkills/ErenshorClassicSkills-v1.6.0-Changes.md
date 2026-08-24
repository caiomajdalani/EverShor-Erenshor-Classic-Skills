# Erenshor Classic Skills v1.6.0

## Compatibility

- Built and tested against Erenshor Steam build **24247002**, released July 17,
  2026.
- Supports BepInEx 5 and Lunaris through Lunaris's legacy-plugin wrapper,
  including the current auto-updated Lunaris 0.1.8 runtime.
- Upgrades directly from v1.5.0; existing skill, inventory-sidecar, recipe, bag,
  and corpse-run data continue from the existing detected data root.

## New: Playable Necromancer

- Added Necromancer to Erenshor's native character-creation class selector,
  character preview, save data, reload path, and starting-item flow.
- Uses the same **52-point class-benefit budget** as native classes, with a
  caster/pet distribution of INT 14, CHA 9, END 9, WIS 7, AGI 6, DEX 4, STR 3.
- Added **35 learnable spells**, matching the July 2026 Reaver's 35-ability
  catalog, plus two hidden secondary DoT effects used by combo spells.
- Added **35 persistent teaching scrolls**, sorted by required level.
- Added support for the native Arcanist-compatible cloth/caster equipment pool
  instead of creating a separate incompatible armor ecosystem.
- Added `Necromancer.Enabled` (Lunaris: `NecromancerEnabled`, default `true`) so
  the class, spells, pets, and merchants can be disabled as one feature.

### Spell progression

| Level | Spells |
|---:|---|
| 1 | Bone Thrall I, Siphon Spark, Rotting Fever |
| 2-7 | Bone Armor I, Clinging Shadow, Mend Bones, Noxious Bolt, Fear, Gather Shadows |
| 8-14 | Bone Thrall II, Venomous Blood, Dark Covenant I, Heart Flutter, Siphon Soul, Bone Armor II, Reclaim Servant |
| 15-18 | Deathly Guise, Leeching Curse, Intensify Death, Bone Thrall III |
| 19-24 | Infectious Cloud, Dark Covenant II, Screaming Terror, Scent of Decay, Bond of Death, Bone Thrall IV |
| 25-30 | Soul Transfusion, Bone Armor III, Devouring Darkness, Drain Spirit, Funeral Pyre |
| 32-35 | Bone Thrall V, Dread Shield, Harmshield, Plague of Souls |

The kit covers poison/void/elemental damage-over-time spells, lifetaps, direct
damage, fear, movement and resistance debuffs, self armor, pet healing and
buffs, health-to-mana conversion, mana transfer, feign death, pet reclaim,
damage shielding, temporary invulnerability, and five pet tiers. Mana costs,
damage, duration, cooldown, resistance, and pet scaling were checked against
native spell and class envelopes.

## New: Real spell VFX and icons

- Added **16 original native-style icon themes** covering the full spell
  catalog.
- Added **74 runtime particle prefabs**: a charge and resolve effect for every
  learnable or internal Necromancer spell definition.
- Added distinct bone, blood, curse, disease, drain, fear, fire, cloud, shadow,
  ward, death, and healing treatments with appropriate color, shape, burst,
  trails, noise, and lifetime behavior.
- Direct hostile spells use moving projectile VFX where appropriate.
- DoTs, defensive wards, clouds, and status effects can attach persistent VFX
  to their target for the active duration and cleanly replace/expire old copies.
- VFX prefabs live under a protected runtime root and stop/clear safely instead
  of leaking particles across scenes.

## New: Armed Bone Thralls

- Added five Bone Thrall tiers using Erenshor's native skeletal companion.
- Thralls inherit a controlled level/stat curve from their Necromancer and use
  owner faction, ally, charm, assist, and nameplate behavior.
- Weapons appear in the correct weapon hand; shields and off-hand weapons appear
  in the correct off hand using the active skeleton's animated hand bones.
- Equipped weapon damage and shield armor feed the pet's real combat stats.
- Added pet healing, temporary pet buffs, reclaim, dismissal/death cleanup, and
  safe resummoning behavior.

### Exact-state pet equipment safety

- Pet weapons and shields use a per-character, crash-safe escrow rather than
  trusting the transient summoned NPC inventory.
- Saves item ID/name, raw quality, and **76 supported scalar fields**, allowing
  compatibility with mods that improve or otherwise modify gear instances.
- Rejects invalid version, quality, payload, or unresolved-item data rather than
  loading unsafe state.
- Uses atomic writes, a last-known-good backup, transaction journaling, startup
  reconciliation, and inventory rollback.
- Trading replacements returns displaced gear safely. Returning all pet gear
  first requires enough direct inventory space, then verifies each exact
  item/quality appeared once before clearing escrow. A failed postcondition
  rolls the whole transaction back.

## New: Necromancer spell merchants

- **Ossira the Bone Reader** — Stowaway's Step.
- **Morthane the Grave Tutor** — Willowwatch.
- **Veyra of the Azure Crypt** — Port Azure.
- Each uses Erenshor's native merchant window and sells exactly 35 level-sorted
  Necromancer scrolls.
- Fixed the generic crafting-recipe injector so it cannot append crafting
  recipes/materials to these curated class vendors.

## Additional fixes and QA improvements

- Fixed Lunaris's sectionless legacy-config mapping aliasing the two generic
  `Enabled` settings. Lunaris now receives distinct `CorpseRunEnabled` and
  `NecromancerEnabled` entries, so the default-disabled corpse mode can never
  silently disable the Necromancer catalog and merchants.
- Ironhide Gauntlets now use the live `ArmoredGlove` model on both hand roots;
  the previous `Palmwraps` choice was absent on some player rigs.
- Test-mode chat output is mirrored into the BepInEx log for durable automated
  evidence.
- Added controlled Necromancer creation, catalog, balance, learning, casting,
  VFX, pet-equipment, travel, and vendor probes.
- Added a stable front/back pet-equipment capture which freezes AI rotation,
  hides the local player model, and restores all state afterward.
- Added exact raw-quality inventory probes and full-inventory recovery testing.

## In-game verification

All checks used the release DLL in the real July 17 Erenshor build, not a mock
Unity project.

- Release build: **0 warnings, 0 errors**.
- Final full smoke: **1,307 passed, 0 failed**.
- Catalog coverage: **781 items** and **280 recipes**.
- Equipment coverage: **176/176** slot/equip checks; all **114 model-rendered**
  items manually reviewed in paired front/back screenshots, including every
  weapon and shield. No hot-pink/missing-material gear remained.
- Necromancer VFX preview: **35/35** learnable spell effects captured.
- Real combat cast sweep: **35/35** spells started/resolved successfully with
  visible VFX and gameplay results.
- Character creation and reload preserved the Necromancer class and all 35
  learned spells.
- All three live city merchants opened in the native vendor window with exactly
  35 correctly sorted scrolls.
- A real native pet trade equipped a Crude Iron Dagger at raw quality **17** and
  a Boneweave Shield at raw quality **23**. Both survived restart/resummon,
  rendered in the correct hands, and changed pet damage **6 -> 11** and armor
  **24 -> 32**.
- With inventory full, pet-gear return was blocked without mutation and both
  items remained in escrow. After space was freed, each returned exactly once,
  escrow became empty, and qualities remained **17** and **23**.
- Final runtime log contained no Classic Skills errors or game exceptions.
- The exact release DLL also completed native login and world entry under both
  loaders. BepInEx and Lunaris each reported 69/69 patch classes, 35 learnable
  spells, 35 scrolls, a Necromancer player, and one Stowaway merchant with all
  35 scrolls in level order. Lunaris resolved its loader-neutral data path and
  the final Lunaris/BepInEx verification logs contained no errors.

## Installation / upgrade

### BepInEx 5

Extract the BepInEx release ZIP into the Erenshor game directory so the DLL is
at `Erenshor/BepInEx/plugins/ErenshorClassicSkills.dll`. Replace the older DLL.

### Lunaris

1. Remove any older Classic Skills DLL from `Erenshor/plugins/`.
2. Download the standalone Lunaris DLL, rename it to
   `ErenshorClassicSkills.dll`, and place it in `Erenshor/plugins/`.
3. Do not keep a second copy in `BepInEx/plugins/` and do not install the
   standalone Attack Sync Fix DLL.
4. Enable **Erenshor Classic Skills** from Lunaris's plugin installer or with
   `/lunaris plugins` at the main menu.
5. Restart Erenshor once, then enter a character.

Use the addon's **Options** page in Lunaris for settings. Lunaris stores those
settings in the binary `plugins/config/erenshorclassicskills.lpcfg`; do not edit
that file as text.

A fresh Lunaris install uses `Erenshor/plugins/config/ClassicSkills/`; BepInEx
uses `Erenshor/BepInEx/config/ClassicSkills/`. When changing loaders, the addon
automatically follows the location that already contains its durable JSON data
so character data is not split.
