# Erenshor Classic Skills v1.5.0

Native-interface, skill-bonus, tooltip, and optional Corpse Run update for
Erenshor Steam build **24247002** (July 17, 2026). This document covers changes
made after v1.4.2.

## Native Skills and Tradeskills interface

- Rebuilt the Skills and Tradeskills windows with Erenshor's native framed-page
  graphics, fonts, colors, dividers, buttons, hover states, and scrollbar art.
- Replaced the addon's former glowing Skills button with a circular native-style
  menu icon matching the surrounding Erenshor menu buttons.
- Added working close buttons to both windows.
- Added clipped, functional vertical scrolling to the Skills list, Active
  Bonuses list, and recipe list.
- Corrected skill progress fills so they remain inside their tracks and display
  proportional progress instead of an oversized highlight.
- Aligned the **Recipes** heading with the selected tradeskill heading on the
  facing page.
- Kept the selected recipe's details, requirements, chance, and combine controls
  visible and readable when a recipe row is clicked.
- Added a consolidated **Active Bonuses** page. It includes earned bonuses from
  utility skills, every combat weapon skill, and all four magic schools, while
  omitting skills with zero points.
- Added native UI hierarchy diagnostics and automated checks for the new
  controls, scrollbars, progress clipping, headings, and bonus rows.

## Skill and tooltip corrections

- Conjuration now increases both damage-over-time effects and damage dealt by the
  player's summoned pets at **0.08% per skill point** (maximum +14.4% at 180).
- Applied Conjuration scaling to both physical and spell damage from a live pet
  owned by the player. Other NPCs, SimPlayers, and another caster's summon are
  explicitly excluded.
- Updated the Conjuration tooltip and Active Bonuses text so both describe the
  same DoT-and-pet behavior.
- Made magic-school and weapon-skill tooltip injection idempotent. Holding right
  click, reopening an item, or allowing Erenshor to rebuild rich text no longer
  repeats the skill/school label down the screen.
- Preserved tooltip tags across native markup rebuilds and excluded consumables
  from inappropriate magic-school tagging.

## Optional Corpse Run mode

- Added an opt-in Corpse Run mode. It is disabled by default and can be changed
  with `/corpserun on`, `/corpserun off`, or the `Corpse Run.Enabled` config
  setting.
- On death, the addon can retain carried inventory, equipped gear, cosmetic
  equipment, aura, charm, crafted-bag contents, and gold on one recoverable
  corpse.
- The corpse appears as a static copy of the character at the moment of death,
  including the visible armor, shield, weapon, enabled model pieces, and material
  colors that were active before belongings were removed.
- Corpse bodies are baked into inert meshes. They have no cloned gameplay
  scripts, animation state, live skeleton references, or colliders, so they
  cannot move with the respawned player or execute duplicated character logic.
- The saved appearance survives game restarts. Marker rotation is reduced to
  horizontal yaw, and reconstructed bounds are validated to prevent tilted,
  microscopic, or giant bodies. If a future game model cannot be reconstructed
  safely, the native corpse chest is used as a recovery fallback.
- Pressing the native loot key at the body creates one withdrawal-only
  **Corpse Bag** containing every saved item. Players may remove items but can
  never add items to this bag. It removes itself after the final item is taken.
- Exact native item instance integers are preserved, including values used by
  gear-improvement mods. Values are not clamped or reinterpreted during death,
  persistence, recovery, or Corpse Bag withdrawal.
- Crafted bags inside the corpse retain their identities and complete contents.
  Nested-bag restrictions remain enforced.
- Recovery requires one safe direct-inventory slot for the Corpse Bag. A full
  inventory refuses recovery without changing the corpse, inventory, bag data,
  or gold.
- A second death cannot overwrite an outstanding corpse or remove another set of
  belongings.
- `/corpserun status` reports the journal, zone, saved stack/gold totals, visual
  type, and visible mesh count. `/corpserun summon` safely relocates an
  unreachable corpse near the player without changing its contents.

## Corpse and inventory safety

- Added a versioned, validated transaction journal with **Prepared**, **Stored**,
  and **Recovering** states.
- Records both sides of death and recovery using exact inventory and bag
  fingerprints so interrupted operations can be classified deterministically.
- Uses atomic writes and verified backups before mutating live inventory.
- Synchronizes the addon inventory sidecar only after Erenshor confirms its
  native save succeeded.
- Rolls a partial recovery back to the exact pre-recovery inventory and bag state
  when possible; ambiguous or invalid state safety-locks further mutation instead
  of guessing.
- Refuses operations when bag moves are pending, save data is not fully loaded,
  item identity no longer resolves exactly, stack bounds are invalid, recovery
  would overflow gold, or a journal cannot be written and revalidated.
- Keeps an already-created corpse recoverable if the mode is later disabled.

## Controlled test-mode additions

- Added `/cstest corpse <die|redie|status>` to exercise the real death path,
  outstanding-corpse protection, and marker diagnostics.
- Added `/cstest capacity <fill|clear|status>` for non-destructive full-inventory
  recovery checks.
- Added `/cstest pet <summon|probe|attack|status>` to verify player-pet ownership
  and Conjuration physical/spell damage scaling.
- Added `/cstest uidump` to capture native Erenshor UI hierarchy evidence.
- Expanded the full smoke suite with corpse serialization, exact modded-gear
  state, Corpse Bag withdrawal rules, one-slot recovery gating, full-inventory
  refusal, crash reconciliation, visual snapshot round trips, horizontal corpse
  orientation, Conjuration pet hooks, tooltip idempotence, and native UI checks.

## Verification performed

- Built against Erenshor Steam build **24247002** and Unity **2021.3.45f2**.
- Release build completed with **0 warnings and 0 errors**.
- Full in-game smoke report: **1,265 passed, 0 failed**.
- Exercised **746 items** through native inventory and crafted-bag round trips.
- Validated **280 recipes**, including stable normal-stock-first and
  recipe-scrolls-last merchant ordering.
- Equipped and inspected **176/176** gear items, including all addon weapons and
  shields; every declared slot, equipped slot, and available model renderer
  passed, and original equipment/vitals were restored and saved afterward.
- Live Corpse Run transaction recovered **17 item stacks and 13 gold** into one
  withdrawal-only Corpse Bag, with a deliberately modified gear-state integer of
  `987654321` retained exactly.
- Confirmed recovery refusal with a full inventory produced byte-identical
  inventory, bag, and corpse records.
- Confirmed second-death protection, restart persistence, drag-out recovery,
  rejection of drag-in attempts, automatic final-item bag removal, and corpse
  journal cleanup.
- Confirmed the saved body persisted through a full restart with **15 visible
  meshes, 0 missing model paths**, normal bounds, the saved armor, and the saved
  weapon. Activating it completed recovery and removed the body only after the
  protected save transaction committed.
- Release DLL SHA-256:
  `a7b8acd4a687baa2a4d7822f2469bbcd1504ced38c34ff6ed2184acd13559ed6`.

## Installation

Choose one loader; do not run the BepInEx and Lunaris bootstraps together.

### BepInEx 5

Place `ErenshorClassicSkills.dll` at:

```text
Erenshor/BepInEx/plugins/ErenshorClassicSkills.dll
```

### Lunaris

1. Install and launch Lunaris once.
2. Place `ErenshorClassicSkills.dll` at
   `Erenshor/plugins/ErenshorClassicSkills.dll`.
3. Enable **Erenshor Classic Skills** in Lunaris's plugin interface or with
   `/lunaris plugins`.
4. Enable the addon before entering a character whenever possible so native
   `CS_` item IDs are registered during character loading.

The DLL embeds its icons and station models; no separate asset folder is needed.
Both loaders deliberately discover existing Classic Skills character data so
switching loaders does not create a second set of skills, recipes, bags, or
recovery files.
