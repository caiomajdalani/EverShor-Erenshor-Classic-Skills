# Erenshor Classic Skills v1.7.0

## Compatibility

- Built for and tested in Erenshor Steam build **24247002**, released July 17,
  2026. Steam reports the installation fully updated.
- Supports BepInEx 5 and Lunaris through Lunaris's legacy-plugin wrapper.
- Upgrades directly from v1.6.0. Existing skills, learned recipes, crafted
  bags, inventory sidecars, corpse records, Necromancer spells, and pet-gear
  escrow continue from the detected loader-neutral data root.

## Necromancer Ascensions

Added a complete five-perk class tab using Erenshor's native Ascension UI,
allocation rules, effect hooks, and save entries. Every perk has three ranks:

- **Grave Mastery** — 5% more damaging-spell damage per rank.
- **Virulent Afflictions** — 33% DoT critical chance per rank.
- **Bone Sovereignty** — Bone Thralls gain one level per rank, including
  level-appropriate health and offense.
- **Unyielding Affliction** — 10% more resist bypass per rank.
- **Swift Decay** — 10% shorter Necromancer spell cooldowns per rank.

Class ranks contribute to the native points-spent counter and survive a full
game restart. The class tab reports the real unspent-point value rather than a
stale UI copy.

## Level-1–35 balance pass

- Rebalanced the full 35-spell progression against the current Arcanist,
  Druid, and Reaver offensive envelopes instead of checking only isolated
  capstone values.
- All 35 level checkpoints now remain between **52% and 124%** of the native
  peer median, inside the guarded 50%–150% band.
- Necromancer still has exactly 35 learnable abilities, matching Reaver at the
  July 2026 level cap, and keeps the same 52-point starting-stat budget as the
  native classes.
- Tuned damage, DoT totals, lifetaps, mana costs, health-to-mana exchange,
  durations, cooldowns, pet heals, pet buffs, and the Plague of Souls capstone.
- Mend Bones now scales with both spell rank/owner level and the active
  Thrall's maximum health, so it stays useful without becoming a full heal.
- Added automated efficiency guards for direct damage, total DoT damage,
  health-to-mana conversion, spell-count parity, class-stat budget, and every
  level checkpoint.

## Bone Thrall model, stats, and equipment

- Bone Thralls now prefer Erenshor's barer **Summoned Skeleton** prefab rather
  than the more heavily dressed Fallen Warrior fallback. Catalog validation
  fails if the intended skeleton cannot be selected.
- Replaced inherited/transient NPC values with deterministic tier profiles for
  level, health, base damage, attack delay, attributes, resists, and armor.
- Each tier was benchmarked below comparable native Druid pets at its unlock
  level. A dedicated validator covers all five tiers.
- Added a persistent stat-curve patch so native recalculation cannot silently
  erase a Thrall's intended health or gear contributions.
- Weapon, shield, and modified-item stats are now bounded by Thrall tier. Exact
  item state is still saved, but unusually powerful upgraded gear cannot turn a
  low-tier pet into an out-of-band companion.
- Weapon and shield models continue to render on the animated right/left hand
  roots. Live validation now checks exact quality, 76 supported scalar fields,
  damage, armor, model placement, serialization, and lighting.
- Skeleton-attached lights are clamped to a subtle range/intensity and have
  shadows disabled, preventing an equipped pet from flooding the scene with
  light or introducing avoidable shadow cost.
- Added a crash-safe QA loadout transaction using Copper Sword quality 8 and
  Bark Shield quality 15. It uses the same external mutation journal and
  rollback path as a player trade; it does not bypass persistence safeguards.

## Cleaner combat feedback

- Necromancer action results now go to Erenshor's combat log rather than
  ordinary chat.
- Removed the repeated `Necromancer` prefix from Mend Bones, covenants, pet
  reclaim/death, feign death, Soul Transfusion, pet buffs, Harmshield, and pet
  trade notices.
- Test-mode combat notices are still mirrored to the loader log so automated
  runs retain durable evidence.

## Expanded controlled QA mode

- The dedicated test character is now named **John EverQuest**. F8 can safely
  select/create slot 2, refuse any differently named occupied slot, and enter
  the world without touching normal characters.
- Added `/cstest necro level 1-35`, Ascension grant/exercise, transactional pet
  loadout/inspect/return, stronger catalog/balance status, and QA identity
  guards. Necromancer mutation commands refuse to run on other characters.
- Controlled spell casts can temporarily meet a spell's mana requirement so
  the real level-32 Tier V summon path can be tested without rewriting John's
  normal build.
- Added `/cstest skillprogress <name> <0-100>` for exact within-level progress
  bar checks.
- Added `/cstest tradeui <skill> <top|bottom>` to expose the complete recipe
  list without changing learned recipes, select a real recipe row, populate its
  detail panel, and exercise both native scrollbar boundaries.
- Added `/cstest vendor <stock|recipes>` to open a standard native merchant on
  either side of the stock/recipe boundary and enforce strict recipes-last
  ordering.
- Added `/cstest icons` to render all 280 crafted output icons in six labeled
  in-game catalogs and validate their runtime textures, alpha, dimensions,
  uniqueness, and recipe-name mappings.
- Added `/cstest simitems` to perform a reversible live SimPlayer test of
  crafted armor, offhand gear, saved item IDs, and one-time Food/Drink use.
- The smoke report now covers the five-perk Ascension tree, dedicated QA
  identity, exact-state pet persistence, three-city spell vendors, all four
  magic-skill multipliers, native menu buttons, boss-only Necromancer spells,
  crafted offhands, SimPlayer item use, and all 280 crafted icons.

## Native menu bar and Extra Settings

- Skills is now a real child of Erenshor's native menu-button container and
  uses the current game's `Shield_eq_icon_r` art, hover state, click treatment,
  sizing, and spacing.
- Added a neighboring **Extra Settings** native menu button using the current
  `Options_eq_icon_r` art. It no longer borrows or compares against the retired
  Skills icon.
- Extra Settings keeps chat/combat presentation separate from the Skills UI. It
  exposes 23 readable color categories plus the **Show damage type** toggle for
  Void, Elemental, Physical, and other resolved damage labels.
- Its category list is now clipped to an exact 16-row viewport. The scrollbar
  still reaches all 23 categories, while the last row and scroll thumb remain
  fully inside the native frame at the bottom boundary.

## Necromancer boss and raid spell acquisition

- Eight existing high-level Necromancer spells are now drop-only instead of
  crowding the spell vendors: Soul Transfusion, Devouring Darkness, Drain
  Spirit, Gravefire Immolation, Ossuary Champion, Dread Shield, Harmshield,
  and Plague of Souls.
- Their scrolls were added idempotently to 24 verified named, boss, and Planar
  raid loot sources. Dread Shield, Harmshield, and Plague of Souls use native
  level-42 raid-scroll metadata while retaining their intended learn levels.
- Vendor reconciliation removes those eight scrolls from all three
  Necromancer merchants without changing the 35-spell class kit or its damage
  balance.

## SimPlayers and crafted equipment

- All 156 crafted equippables now carry explicit, native-style class
  restrictions instead of empty or universal class lists. Heavy armor and
  two-handed melee weapons stay with martial classes; bows stay with
  Stormcallers; cloth, caster accessories, daggers, staves, and mystical
  shields cover the appropriate caster/hybrid classes. Necromancer inherits
  only Arcanist-compatible profiles, so it cannot wear plate or wield a
  two-handed sword.
- SimPlayers can accept compatible crafted gear, reject incompatible gear, and
  save, reload, evaluate, and equip upgrades through their native
  inventory/equipment records and `CS_` item-ID resolution. Necromancer also
  has a guarded crafted-upgrade scoring path because the native upgrade method
  has no Necromancer branch.
- Crafted Food and Drink are quantity-backed Sim inventory items. Sim AI uses
  at most one of each category when its effect is absent, decrements only after
  the effect is proven active, saves immediately, and rolls back on failure.
  Their click effects are removed from the native infinite-spell AI list, which
  prevents free repeated casts and duplication.
- Thirteen lightweight crafted one-handed weapons can now use either Primary
  or Secondary. Every selected model is verified beneath both live hand trees;
  two right-hand-only inherited models were replaced after the first smoke pass
  exposed them. Two-handers, bows, staves, heavy weapons, and shields retain
  their original slot rules.

## Crafted item icon overhaul

- Repainted every one of the 280 recipe-output icons in Erenshor's native
  high-fantasy inventory style with clear silhouettes and material-specific
  details. No recipe outputs share an icon.
- Added dedicated art for the nine fish dishes and five bows that previously
  had no PNG and fell through to borrowed or procedural visuals.
- Every production asset is a unique readable 64x64 RGBA PNG with transparent
  corners, a real alpha silhouette, and zero green-screen spill. The same set is
  embedded in the DLL and supplied through the external override directory for
  existing installations.

## In-game verification

All checks ran inside the real Steam game, using the installed release-candidate
DLL rather than a mock Unity project.

- Release build: **0 warnings, 0 errors**.
- Final full smoke: **1,343 passed, 0 failed**.
- Catalog coverage: **791 items** and **280 recipes**.
- Equipment coverage: **185/185** correct declared/equipped slots, with
  **302** front/back screenshots and a 185-row all-PASS manifest. Weapons,
  bows, shields, armor palettes, and attachment points were visually reviewed;
  no missing-material magenta flood remained.
- Crafted icon catalog: **280/280** runtime icons rendered in six labeled
  in-game pages; 8/8 checks passed with no missing mappings, duplicate pixel
  fingerprints, opaque corners, blank icons, bad dimensions, or chroma spill.
- SimPlayer live test: crafted armor and an offhand weapon equipped through
  native logic and resolved after save; the real trade handler accepted crafted
  consumables; one Food and one Drink each decremented from 2 to 1; native
  checks rejected plate and a two-handed sword for Necromancer; the exact
  pre-test Sim state was restored.
- Extra Settings visual test: **12/12 passed**. Both top and bottom scroll
  boundaries were captured from the shipping DLL, all 23 categories remained
  reachable, and no row or scrollbar geometry extended below the window.
- Skills UI: a real 90/180 Foraging skill with 50% within-level XP showed an
  exactly half-width fill clipped inside its native frame. Close control and
  vertical scrollbar were present.
- Tradeskills UI: a real 90/180 Smithing skill showed the same contained 50%
  fill; `Recipes` and `Smithing` share the title baseline; selecting Copper
  Rivets populated icon, description, requirements, chance, materials, and
  value; the 52-recipe scrollbar reached both top and bottom, where Ironheart
  Champion's Blade populated its details.
- Native merchant ordering: Aldoa Manday showed 60 ordinary/material entries
  before 61 recipe scrolls, with stock on page 5 and recipes beginning on page
  6. No recipe appeared before the boundary.
- Necromancer spell vendors: Ossira, Morthane, and Veyra each opened in their
  live city with the remaining 27 vendor spells plus five auras, correctly
  level-sorted after the eight boss-only scrolls were removed.
- Boss acquisition: eight drop-only Necromancer spells passed against 24 live
  loot sources, including three native-metadata Planar raid drops; duplicate
  injection and vendor-exclusion guards passed.
- Real spell sweep: **35/35** Necromancer spells started and resolved with
  gameplay effects and VFX.
- Ascensions: eight native general ranks plus one rank in each of the five new
  class perks were allocated through the native UI and persisted after restart.
- Tier V Bone Thrall: exact Copper Sword quality 8 and Bark Shield quality 15
  state survived restart/resummon, rendered in the correct hands, affected
  live damage/armor, returned exactly once, and left escrow empty. A second
  return was safely a no-op.
- Final runtime log contained no Classic Skills error or exception. The smoke
  harness restored John's original equipment, vitals, inventory, and native
  save successfully.

## Installation / upgrade

### BepInEx 5

Extract the BepInEx ZIP into the Erenshor directory. Replace the old DLL so the
result includes:

`Erenshor/BepInEx/plugins/ErenshorClassicSkills.dll`

`Erenshor/BepInEx/plugins/ClassicSkills/Icons/`

### Lunaris

1. Install and launch Lunaris once.
2. Remove any older Classic Skills DLL from `Erenshor/plugins/`.
3. Extract the Lunaris ZIP into the game directory so it installs
   `Erenshor/plugins/ErenshorClassicSkills.dll` and the refreshed
   `Erenshor/plugins/ClassicSkills/Icons/` directory. A standalone DLL works
   for fresh installs, but the ZIP is recommended when upgrading an older icon
   pack.
4. Do not leave another copy in `BepInEx/plugins/`, and do not install the
   standalone Attack Sync Fix DLL because Classic Skills already includes it.
5. Enable **Erenshor Classic Skills** in Lunaris's plugin installer or with
   `/lunaris plugins`, restart Erenshor once, and enter a character.

Lunaris settings are managed through the addon's **Options** page. Its binary
`.lpcfg` file is not intended for manual text editing. Fresh Lunaris data lives
under `Erenshor/plugins/config/ClassicSkills/`; BepInEx data lives under
`Erenshor/BepInEx/config/ClassicSkills/`. When switching loaders, the addon
automatically follows the location that already contains the durable JSON data.

## Release safety

Controlled test mode and debug chat commands are disabled in the shipped
configuration. They must be explicitly enabled for future QA.
