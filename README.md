# EverShor - Classic Skills, Classes and Features

**EverShor is an EverQuest-inspired gameplay mod for Erenshor.** It adds skills that grow through use, crafting and gathering, a player-driven SimCraft economy, new equipment and consumables, optional corpse recovery, and two playable classes: **Necromancer** and **Monk**.

Train a weapon or magic school, fish and forage for ingredients, make equipment at crafting stations, ask a Sim to craft for you, or play a custom class with its own progression, trainers, gear, auras and Ascensions. Features can be enabled or tuned through the mod's in-game settings.

The mod contains **25 trainable skills**: seven utility/gathering skills, eight weapon skills, four magic schools and six crafting tradeskills. Fishing is counted among the seven utility skills, even though it is also a tradeskill in everyday use. The six crafting disciplines contain **280 recipes**.

## Contents

- [Installation](#installation)
- [Skills and progression](#skills-system-25-skills)
- [Crafting, consumables and bags](#tradeskill-crafting)
- [Optional corpse recovery](#optional-corpse-run)
- [Necromancer, spells and Thralls](#playable-necromancer)
- [Monk and techniques](#playable-monk)
- [SimCraft](#simcrafting--whisper-based-player-crafting-economy)
- [Materials and gathering](#material-economy)
- [Commands](#chat-commands)
- [Keybinds](#default-keybinds)
- [Configuration](#configuration)
- [What changed in 1.9.1](#what-changed-in-191)
- [What changed in 1.9.0](#what-changed-in-190)

## Installation

This version uses **Lunaris**. Install Lunaris for Erenshor and launch it once to create the game's `plugins` directory.

1. Back up your character saves and ClassicSkills data before migrating from an older version.
2. Close Erenshor. Remove the previous Classic Skills plugin from its old loader directory.
3. Copy **one** `ErenshorClassicSkills.dll` into `<Erenshor>/plugins/`.
4. Open Lunaris, enable **EverShor - Classic Skills, Classes and Features**, and review **Options**.
5. Enable the mod before loading a character so its custom items and classes are available. Restart after initial enablement.

The standalone Attack Sync Fix must not be installed alongside the integrated version. This native build does not load directly through BepInEx. Existing ClassicSkills JSON data can be discovered in the legacy data directory, but previous BepInEx option values are not automatically imported. Review your settings after migration.

## Choosing a custom class

On character creation, use the **EverShor** page button to see **Necromancer** and **Monk**. Return to **Native** for the base game's classes. This keeps extra class buttons out of the native grid and leaves room for appearance and proficiency controls.

An optional Fate of the Fallen page bridge coordinates its class pages with EverShor's page. The mod does not require Fate of the Fallen. Page layout across UI scales and mod load orders still needs in-game validation.



### Skills System (25 Skills)

Press **F8** (configurable) to open the Skills window. Spend a Training Point to unlock a skill at level zero before progressing it through use; optional catch-up settings can initialize existing characters. Skills level up through use via an EQ-style skill-up chance system with a trivial cap. Training Points (3 per player level) can be spent to manually raise skills. Skill cap formula: `(PlayerLevel × 5) + 5` — max 180 at level 35.

The Skills and Tradeskills windows use Erenshor's native framed-page artwork,
fonts, buttons, scrollbars, close controls, and clipped progress bars. The Skills
window also consolidates every earned utility, combat, and magic bonus into an
**Active Bonuses** panel; skills at zero are omitted so the list stays readable.
The Skills and Extra Settings buttons are real children of the game's native
menu-button bar. They use Erenshor's current shield/options art, sizing,
spacing, hover state, and click treatment. Extra Settings keeps the 23 chat and
combat-log color categories plus the damage-type label toggle out of the Skills
window.

**Utility Skills (7):**
- **Fishing** — XP on every catch; higher skill = more nibbles = better catch rate
- **Foraging** — Gather herbs and materials from the wild (F9 keybind or leaf HUD icon)
- **Swimming** — Levels from swimming; increases swim speed (+0.3% per level)
- **Bind Wound** — Heal yourself out of combat (+0.15% max HP per level, F10 keybind or bandage HUD icon)
- **Meditate** — Automatically regenerates extra mana while sitting safely out of combat
- **Food Tolerance** — Extends food/drink buff duration (+0.5% per level)
- **Begging** — Ask NPCs for gold, items, or lore (; keybind or coin HUD icon)

**Combat Skills (8):**
- 1H Slashing, 1H Blunt, Piercing, 2H Slashing, 2H Blunt, Archery, Wands, Hand to Hand
- Each weapon skill adds bonus damage: +0.085% per level (up to +15.3% at 180)
- Weapon skill type shown on all weapon tooltips

**Magic Skills (4):**
- **Evocation** — Levels on damage spells. +0.08% spell damage per level
- **Abjuration** — Levels on buff spells. +0.3% buff duration per level
- **Alteration** — Levels on heals. +0.08% heal amount per level
- **Conjuration** — Levels on DoTs and summoned-pet attacks. +0.08% DoT and
  player-owned pet damage per level

**Tradeskills (6):**
- Smithing (52 recipes), Baking (55), Brewing (46), Fletching (29), Jewelcraft (47), Tailoring (51)
- 280 total recipes crafted at stations placed in Port Azure and Stowaway's Step

---

### Tradeskill Crafting

Craft at appropriate stations. There are custom stations(loom, brewing barrel, oven, jewlers kit, fletching table) for each tradeskill at Port Azure and the castle/Warehouse(in the room to the right) on stowaway island. Campfires and ovens also work for baking. Brew barrels also work for brewing. Workbenches across the game also work for Fletching. Forges work for Smithing. Crafting has a success/fail chance based on skill level vs recipe difficulty. Failed combines consume materials (just like EQ).

**Recipe Sources:**
- **Starter recipes** (a small initial set for each tradeskill) — auto-learned on first load
- **Vendor recipes** (skill 1–90) — bought as scrolls from merchants
- Merchant pages keep normal stock, unlocked quest rewards, and crafting
  materials first; recipe scrolls are grouped on the final pages.
- **Drop-only recipes** (skill 90+) — found in mob loot windows (5% normal, 30% boss)
- **Boss-only recipes** — drop exclusively from named/boss mobs
- Right-click recipe scrolls in inventory to learn them

**Crafting Stations:**
- Custom 3D stations spawned in Port Azure and Stowaway's Step
- Game objects (campfires, stoves, ovens, barrels, workbenches) also work as stations
- The game's existing Forge works for Smithing

**Craftable Equipment (156 pieces):**
- Smithing: Weapons and shields (ilvl 2–24)
- Fletching: Bows (ilvl 1–14)
- Jewelcraft: Rings, necklaces, headpieces, charms (ilvl 1–25)
- Tailoring: Cloth/leather armor, cloaks (ilvl 2–25)

**Craftable Consumables (101 items):**
- Baking: 55 Food recipes scaling from +2 stats to +15 all stats
- Brewing: 46 Drink recipes on the same curve
- Food/Drink use a category system — one Food buff and one Drink buff active at a time

All crafted equipment has native-style class restrictions, and SimPlayers can
accept, evaluate, equip, save, and reload compatible upgrades. Heavy armor and
two-handed melee weapons stay with martial classes; bows stay with
Stormcallers; caster and hybrid equipment follows the corresponding native
class roles. Necromancer receives only Arcanist-compatible crafted profiles, so
it cannot wear plate or wield a two-handed sword. SimPlayers also use
quantity-backed crafted Food and Drink once when the corresponding category is
inactive. Thirteen lightweight one-handed crafted weapons can be equipped in
either Primary or Secondary; their models are verified on both hand trees,
while two-handers, bows, staves, heavy weapons, and shields keep their original
slot rules.

Every one of the 280 recipe outputs has a unique native-style 64x64 RGBA icon.
This includes dedicated art for nine fish dishes and five bows that previously
fell back to borrowed/procedural visuals.

All ordinary addon items—including recipe scrolls, Monk tomes, Necromancer
scrolls, raid-scroll duplicates, and the Necromancer charm mold—can use
Erenshor's native sell, trash, and bank paths.

**Bags:**
- Silken Pouch (4 slots, Tailoring 5)
- Herbalist's Satchel (6 slots, Tailoring 10)
- Simple Backpack (8 slots, Tailoring 25)
- Adventurer's Backpack (10 slots, Tailoring 50)
- Bag identity, contents, inventory movement, crafting consumption, bank pulls,
  and save recovery use a crash-recoverable transaction journal. Empty crafted
  bags can be sold or trashed; filled crafted bags must be emptied before sale
  or destruction so their contents cannot be lost, but they can be banked with
  their identity and contents intact. Bags cannot be nested or opened directly
  from bank/vendor/loot slots.
- Corpse Bags are the sole intentional exception: they are recovery-only and
  cannot be sold, trashed, or banked. Emptying one completes recovery and
  consumes it.
- Right-click a carried bag to open it. Items can be dragged from inventory into
  a bag and from any bag slot back into a chosen inventory slot. The bag window
  remains above the inventory while the carried item icon remains above both.

### Optional Corpse Run

Corpse Run is disabled by default. Enable it in Lunaris Options with
`CorpseRunEnabled=true` (or with `/corpserun on`) to leave your carried inventory, equipped gear,
cosmetics, aura, charm, crafted-bag contents, and gold on death.

- The corpse is a static copy of the character as they looked at death, including
  equipped armor, shields, and weapons. It remains correct across restarts and
  cannot run scripts, collide, animate, or follow the living player.
- Return to the body and press the native loot key (default **E**) to recover a
  withdrawal-only **Corpse Bag** containing every saved item in its exact native
  state. Modded gear quality/state integers are preserved without normalization.
- Items cannot be placed into a Corpse Bag. It disappears after its final item is
  removed.
- A second death never overwrites an outstanding corpse. Full inventory, invalid
  saves, unresolved items, interrupted writes, or partial recovery safely stop or
  roll back instead of losing or duplicating belongings.
- `/corpserun status` reports the transaction and marker state. `/corpserun summon`
  safely relocates an unreachable corpse nearby. `/corpserun off` disables future
  corpse creation without deleting an existing recoverable corpse.

### Playable Necromancer

Necromancer is enabled by default and appears on the EverShor page in
character creation. It is a caster built around damage over time, lifetaps, resource conversion and an equipped undead companion.

- 35 learnable spells from levels 1-35: diseases, poison and void DoTs,
  lifetaps, fear, bone armor, health-to-mana conversion, feign death, pet
  healing/buffs, mana transfer, damage shields, Harmshield, and five Bone
  Thrall tiers.
- Every spell has a native-style icon plus bespoke Necromancer cast and resolve
  VFX. The native caster-surrounding spell effect is suppressed to avoid visual
  stacking and cast-time frame stalls. Direct spells can launch colored
  projectiles; DoTs, wards, clouds, and defensive effects retain visible effects
  for their active duration. Embedded CC0 vapor art gives smoke and disease
  layers irregular, organic edges instead of repeated procedural circles.
- New Necromancers begin with both a Rusty Dagger and Rusty Shortsword so one
  weapon can be placed in the Thrall Bag for the first summoned pet.
- Bone Thralls use skeleton visuals and accept slot-validated equipment,
  including Aura and Charm. Weapons, armor, attributes and item quality contribute
  to their combat values. Equipment visuals use guarded rig mapping.
- Tier-specific attacks supplement gear: Bone Rake, Sentry's Rebuke, Grave Siphon,
  Crypt Breaker and Ossuary Verdict. They do not copy the player's learned spells.
- Direct taunt: 1.25 s; area taunt: 2 s within 12 m, only for NPCs already fighting
  the owner/Thrall. Slow begins at tier 2 (11 s cooldown); stun at tier 3 (15 s).
- Bow-equipped Thralls retain the ranged projectile path. Movement follows the
  owner's effective movement speed. Balance and visuals require game validation.
- Equipment is persisted through character-scoped escrow/Thrall Bag handling.
  Verify zoning, dismissal, death, restart, full-inventory returns and rollback
  using the manual validation matrix before treating migration as release-ready.
- Spell merchants in Stowaway's Step, Willowwatch, and Port Azure—Ossira the
  Bone Reader, Morthane the Grave Tutor, and Veyra of the Azure Crypt—provide
  a tiered progression. Beginner-zone Ossira stops at level 10, Morthane
  teaches through level 26, and Veyra carries the advanced catalog through
  level 35. Each trainer is anchored beside that city's native Arcanist
  trainer rather than an unrelated merchant.
- Eight Necromancer Ascension perks support spell damage, DoT criticals, stronger summons, resistance bypass, cooldown reduction, resistances, mitigation and survival. Their ranks use the native Ascension/save systems; see the perk table below.
- Every Necromancer ability names its own recorded cast and resolve layer drawn
  from a nineteen-file CC0 library, so a bone thrall rises on cracking bone, a
  ward closes on struck metal, a plague lands on a wail, and the pyre ignites on
  fire rather than all of them sharing one generic spell noise. The two
  damage-over-time ticks deliberately carry a resolve layer only, because a
  recorded transient on every tick becomes noise. Breath, grit, and edge
  components are resonant filtered noise rather than sine partials, and every
  cast varies pitch and level so a repeated rotation never retriggers an
  identical waveform. Spell audio also now respects the combat and master volume
  sliders, which it previously ignored. Sources and per-file provenance are in
  `THIRD_PARTY_NOTICES.md`.
- Necromancer action notices are written to the combat log in a consolidated
  format without repeating a `Necromancer` label in ordinary chat.


#### Necromancer spell progression

Learnable spells are listed below. Internal effect-only spells are omitted. Read the in-game tooltip for current damage, mana and duration values.

| Level | Spell | Role | What it does |
|---:|---|---|---|
| 1 | Cavorting Bones | Summon a Bone Thrall | Raises a fragile skeletal warrior. Bone thralls can wield a weapon and carry a shield. |
| 1 | Siphon Spark | Direct damage / lifetap | Damages the target and heals the caster for the damage dealt. |
| 1 | Rotting Fever | Damage over time | Afflicts the target with an efficient disease that worsens over time. |
| 2 | Bone Armor | Defensive or utility buff | Wraps the caster in grave-bound bone for thirty seconds, granting 35 health and 12 armor. |
| 3 | Clinging Shadow | Damage over time | Darkness gnaws at the target and slows its movement. |
| 4 | Mend Bones | Class utility | Repairs at least one quarter of the Necromancer's active bone thrall's maximum health. |
| 5 | Noxious Bolt | Direct damage / lifetap | Hurls a concentrated bolt of grave poison at the target. |
| 6 | Fear | Debuff or control | Fills a non-boss target with terror for up to six seconds. |
| 7 | Gather Shadows | Defensive or utility buff | Draws darkness around the caster, granting invisibility for up to one minute. |
| 8 | Risen Sentry | Summon a Bone Thrall | Raises a sturdier skeletal warrior with improved offense and defenses. |
| 8 | Venomous Blood | Damage over time | Poisons the target with corrupted blood. |
| 10 | Dark Covenant | Class utility | For thirty seconds, sacrifices 8 health to restore 20 mana every three seconds. The sacrifice pauses below 25% health. |
| 11 | Heart Flutter | Debuff or control | Weakens the target's strength and armor while dealing void damage. |
| 12 | Siphon Soul | Direct damage / lifetap | Damages the target and heals the caster for the damage dealt. |
| 13 | Ossified Carapace | Defensive or utility buff | Fortifies the caster with layered bone for one minute, granting 175 health and 60 armor. |
| 14 | Reclaim Servant | Class utility | Reclaims the active thrall for 25% of the caster's maximum mana. Pet equipment remains safely escrowed. |
| 15 | Deathly Guise | Class utility | Drops hostile attention and prevents new NPC aggression for six seconds. Any action breaks the guise. |
| 16 | Leeching Curse | Direct damage / lifetap | Damages the target and immediately heals the caster for the damage dealt, then leaves a lingering void curse. |
| 17 | Intensify Death | Class utility | Empowers the active thrall for thirty seconds: +350 HP, +45 AC, and +20 Strength. |
| 18 | Gravebound Soldier | Summon a Bone Thrall | Raises a veteran skeletal warrior with improved offense and defenses. |
| 19 | Infectious Cloud | Area damage | A burst of plague damages and infects nearby enemies. |
| 20 | Blood Communion | Class utility | For thirty seconds, sacrifices 15 health to restore 75 mana every three seconds. The sacrifice pauses below 25% health. |
| 21 | Screaming Terror | Debuff or control | Overwhelms a non-boss target with terror for up to twelve seconds. |
| 22 | Scent of Decay | Debuff or control | Lowers the target's poison and void resistance. |
| 23 | Bond of Death | Class utility | Strengthens the active thrall for thirty seconds: +900 HP, +90 AC, and +35 Strength. |
| 24 | Crypt Vanguard | Summon a Bone Thrall | Raises an elite skeletal warrior with improved offense and defenses. |
| 25 | Soul Transfusion | Class utility | Transfers 140 mana to another player or grouped SimPlayer at an efficiency loss. |
| 26 | Sepulchral Plate | Defensive or utility buff | Encases the caster in ancient bone for ninety seconds, granting 500 health and 150 armor. |
| 27 | Devouring Darkness | Damage over time | Devouring shadow deals heavy damage and strongly slows the target. |
| 29 | Drain Spirit | Direct damage / lifetap | Damages the target and heals the caster for the damage dealt. |
| 30 | Gravefire Immolation | Damage over time | Cremates the target in persistent grave-fire. |
| 32 | Ossuary Champion | Summon a Bone Thrall | Raises the strongest skeletal warrior available to a mortal Necromancer. |
| 33 | Dread Shield | Defensive or utility buff | Surrounds the caster with vengeful spirits for twelve seconds, returning 150 damage to melee attackers. |
| 34 | Harmshield | Class utility | Makes the caster invulnerable but immobile for six seconds. Any action breaks the shield. |
| 35 | Plague of Souls | Damage over time | The Necromancer's capstone plague ravages body and spirit over eighteen seconds. |


#### Shared native Necromancer skills

These retain their native skill identity and are learned through the appropriate skill books.

| Level | Skill |
|---:|---|
| 8 | Arcane Proficiency |
| 12 | Arcane Recovery |
| 15 | Block |
| 20 | Block II |
| 8 | Dodge |
| 16 | Dodge II |


#### Necromancer Ascension perks

| Perk | Effect per rank | Maximum ranks |
|---|---|---:|
| Grave Mastery | All damaging spells deal 5% more damage per rank. | 5 |
| Virulent Afflictions | Every damage-over-time tick has a 33% chance to critically hit. | 4 |
| Bone Sovereignty | Bone Thralls are 1 level higher per rank, with level-appropriate health and offense. | 3 |
| Unyielding Affliction | Increase the chance for your spells to bypass resists by 10% per rank. | 4 |
| Swift Decay | Necromancer spell cooldowns are 10% shorter per rank. | 3 |
| Shroud of the Grave | Grave-cold wraps you: all resistances increase by 3% per rank. | 3 |
| Deathward | Wards of bone turn aside 5% of all incoming damage per rank. | 4 |
| Defy the Grave | When a killing blow lands you have a 10% chance per rank to refuse it, restoring your health and mana and shrugging off stuns and fear. | 5 |

Turn off `NecromancerEnabled` in Lunaris Options before starting the game if
the class and its vendors should not be added.

### Playable Monk

Monk is enabled by default and appears on the EverShor page in character
creation. Its starting-stat budget, native resource use, equipment restrictions,
and level 1-35 output were built against the same native class envelopes used for
Necromancer rather than modifying any base class.

- 32 uniquely named Monk techniques plus the seven native martial abilities
  shared with other melee classes, for a 39-ability level 1-35 progression.
- Active custom techniques appear in the native spellbook as offensive or beneficial abilities. Every active Monk spell
  consumes level-scaled 5-160 mana; Open Hand Discipline remains a free,
  Skill-only passive.
- Fourteen offensive techniques use actual character motion. Punches retarget an
  authored Erenshor humanoid strike; kicks use native kick/jump states with
  technique-specific turns, weight shifts, spins, lifts, and lunges. Flying Kick
  visibly leaves the ground and travels into the target instead of playing VFX
  while the character stands still.
- Level-5 Pummel delivers a six-hit alternating-fist barrage with flying knuckle
  afterimages, while Rising Dragon Fist explodes from a crouch into an airborne
  uppercut with a double energy vortex, hard contact bloom, interrupt, and brief
  stun.
- Resonant Palm replaces the save-compatible Stone Throw presentation with a
  planted two-palm energy release and traveling teal sphere. Whirlwind Kick
  holds its roundhouse contact silhouette through one smooth, support-foot-
  planted spin with heel-orbit, cyclone, and ground-wake effects.
- Technique VFX are lit for Erenshor's own post-processing rather than drawn
  flat. Erenshor runs Bloom at intensity 10 with a threshold of 1.0, so effect
  cores are driven well past that threshold and the surrounding body is held
  just under it: the glow comes from the game's bloom and the technique's hue
  survives in the halo instead of blowing out to white. Strokes and trails use
  an additive feathered ribbon instead of a matte untextured strip, particles
  are 128px mipped sprites with a hot core rather than 32px discs, rings are
  tessellated as curves rather than visible polygons, and contact lights are
  forced to pixel rendering so they are not silently demoted to vertex lights.
  Where the built-in soft-particle shader is available it is used, which removes
  the hard seam where an effect cuts through a character or the ground.
- Strikes are timed asymmetrically. A technique coils, accelerates into contact
  on a cubic curve, stops hard, then settles back more slowly with a small
  follow-through, and carries a brief counter-rotation before the release so the
  eye reads the coil. Heavy contacts add a short hit-stop on the target's
  animator; global time is deliberately untouched so sim players, pets, and
  native timed systems are never dragged by a player's punch.
- Damage and each technique's bespoke VFX resolve at the authored contact frame.
  An embedded CC0 radial-energy sheet now adds a sharper animated contact bloom
  without replacing the distinct rings, trails, seals, and silhouettes.
  Autoattacks with either handwrap animate as punches and suppress the game's
  generic two-handed weapon states, so a Monk no longer swings an invisible
  sword. Repeated autoattacks alternate right and left punches even when only
  one handwrap is equipped. An active technique cannot be visually overwritten
  by an autoattack.
- Every technique has a unique native-style medallion icon and matching unique
  tome icon. Hand-drawn glyph attributions are recorded in
  `THIRD_PARTY_NOTICES.md`.
  Five auras, twenty-seven class-only gear items (including eight trainer-line, eight boss-only, and six Tailoring-crafted paired handwraps from levels 1-42), and a unique class icon
  have their own artwork as well.
- Five Monk auras follow the native martial-aura progression: Stillwater Breath,
  Flowing Reed, Iron Body, Path of the Gale, and Grandmaster's Presence. Grandmaster's Presence is also usable by Reaver; the other Monk auras keep their class restrictions.
- Eight Monk Ascension perks improve technique damage, Mend, Stone Stance, Hundred Fists, Feign Death, extra attacks, mitigation and resistances. See the perk table below.
- Master Ren of the Open Hand, Sera Windstep, and Grandmaster Kalen sell
  level-sorted techniques, auras, and starter gear in Stowaway's Step,
  Willowwatch, and Port Azure respectively. Each Monk trainer is anchored
  beside that city's native Reaver trainer.
- Class trainers outfit beginners only. A merchant stocks no weapon or armor
  piece above level 10, so the Monk shelf keeps Copper-Ring Handwraps, Jade
  Serpent Bindings, and the Initiate's Headwrap. Charms such as Grandmaster's
  Prayer Beads, every aura, and every technique tome or spell scroll stay
  purchasable at their own level.
- Everything the cap removes from the shelf has a world source at its own level
  band, each one a verified native NPC: Silent-Step Sandals in Krakengard,
  Emberfist Handwraps in Azynthi's Garden, Windstep Gi in Windwashed Pass, Iron
  Temple Cestus in Rockshade Hold, Grandmaster's Legwraps in Loomingwood,
  Moonweave Handwraps from Planar creatures, Fists of the Tranquil Storm from
  Granitus and the Gardener of Loomingwood, and Grandmaster's Sun-and-Moon
  Fists as a legendary drop in Soluna's Landing. Each item pairs a regular
  dungeon mob with a named enemy in the same zone, alongside the eight
  boss-only trophies and six Tailoring wraps that were already drop- or
  craft-only.
- Monk-only gear includes Initiate's Headwrap, Windstep Gi, Silent-Step Sandals,
  Grandmaster's Legwraps, Novice Handwraps, Fists of the Tranquil Storm, and
  Grandmaster's Prayer Beads. Handwraps can be used in either hand while keeping
  the native unarmed combat stance. Equipped handwrap weapons now render as
  independently skinned wrist and fingerless hand wraps on the matching hand.
  The wrap now covers the back of the hand and follows every finger, punch,
  technique, and normal player animation.
- Monk player and Sim behavior is still being corrected. Punch retargeting, approach distance, interactions and class isolation require in-game verification; intended animations are described above, not guaranteed on every setup.


#### Monk technique progression

The custom techniques below are supplemented by seven shared native martial skills. Open Hand Discipline is passive; active techniques use the spellbook and mana.

| Level | Technique | What it does |
|---:|---|---|
| 1 | Open Hand Discipline | Passive Skill: Your handwrap attacks deal 5% more physical damage. Your skill with handwraps grows naturally as you gain levels. |
| 1 | Mend | Focus your breathing to restore 25% of maximum health. Deep Mend improves the amount restored. |
| 1 | Jab | Strike with a fast straight punch that deals physical damage. |
| 3 | Shoulder Check | Drive through the target with a compact body strike that deals physical damage and interrupts spellcasting. |
| 4 | Tiger Claw | Rake the target with a focused claw strike that deals physical damage. |
| 5 | Round Kick | Sweep a fast circular kick into the target for physical damage. |
| 5 | Pummel | Unleash a blur of alternating fists. The opening blow deals physical damage and five rapid afterimage strikes follow it. |
| 6 | Focused Will | Break fear and stun through disciplined focus. This technique can be used while incapacitated. |
| 7 | Resonant Palm | Cup focused martial force at your hip, then drive both palms forward to launch it up to 18 meters, dealing light physical damage and drawing an enemy toward you. |
| 8 | Crescent Heel | Hook the target with a crescent kick, dealing physical damage and slowing its movement and attacks briefly. |
| 9 | Centered Breath | Center your breathing to restore 12% of maximum health over 9 seconds. Damage does not interrupt the recovery. |
| 10 | Feign Death | Collapse convincingly, clearing enemy attention and preventing new aggression for a short time. Taking damage ends the feign. |
| 11 | Countering Palm | Adopt a poised guard for 8 seconds. The first melee attacker to strike you is answered with a retaliatory palm. |
| 12 | Eagle Strike | Drive a precise hand strike into the target for physical damage. |
| 13 | Breath of Harmony | Guide restorative breath through your body, healing 20% of maximum health over 15 seconds. |
| 14 | Silent Step | Muffle your presence for 12 seconds, increasing movement speed and reducing enemy notice. Attacking ends the technique. |
| 15 | Fist of Falling Rain | Unleash a three-beat punch combination. The first blow deals physical damage and two lighter echoes follow it. |
| 16 | Flying Kick | Leap into a crushing kick that deals heavy physical damage. |
| 17 | Reed in the Wind | Yield without breaking, reducing incoming physical damage by 15% for 10 seconds. |
| 18 | Stone Stance | Brace like living stone, reducing incoming damage by 25% for 12 seconds. Flowing Defense improves the reduction. |
| 19 | Sweeping Crescent | Scythe a low kick through every enemy in melee range for physical damage. |
| 20 | Pressure Point | Strike a precise nerve point for physical damage and a short stun. |
| 21 | Gale-Splitting Palm | Compress your breath into a broad cutting wave that travels to a distant enemy and deals physical damage. |
| 22 | Whirlwind Kick | Spin through every enemy in melee range with a physical kick. |
| 24 | Empty Body | Release attachment to pain, reducing non-physical damage by 25% for 10 seconds. |
| 26 | Rising Dragon Fist | Explode from a coiled crouch into a fierce rising uppercut, dealing heavy physical damage, interrupting spellcasting, and briefly stunning the target. |
| 27 | Vaulting Tempest | Compress into a low guarded crouch, then spring through a rising somersault kick and crescent wave for heavy physical damage that interrupts spellcasting. |
| 28 | Purify Body | Purge detrimental magical effects from your body without removing beneficial effects. |
| 30 | Hundred Fists | Enter a focused flurry, reducing main-hand and off-hand attack delays by 25% for 20 seconds. Lingering Flurry extends the duration. |
| 31 | Five-Point Focus | Align breath, stance, sight, rhythm, and intent. Monk technique damage is increased by 15% for 12 seconds. |
| 34 | Skybreaker Rush | Rush into the enemy behind a guarded step, plant a driving punch, then release a sliding ground-level storm explosion for exceptional physical damage. |
| 35 | Quivering Palm | Focus every motion into one devastating palm strike. Deals exceptional physical damage on a long cooldown. |


#### Shared native Monk skills

These retain their native skill identity and are learned through the appropriate skill books.

| Level | Skill |
|---:|---|
| 1 | Kick |
| 2 | Dodge |
| 2 | Dual Wield |
| 6 | Double Attack |
| 8 | Dodge II |
| 24 | Dodge III |
| 32 | Dodge IV |


#### Monk Ascension perks

| Perk | Effect per rank | Maximum ranks |
|---|---|---:|
| Way of Iron | Monk techniques deal 15% more damage per rank. | 5 |
| Deep Mend | Mend restores an additional 5% of maximum health per rank. | 4 |
| Flowing Defense | Stone Stance reduces incoming damage by an additional 3% per rank. | 4 |
| Lingering Flurry | Hundred Fists lasts 3 seconds longer per rank. | 3 |
| Perfect Stillness | Feign Death lasts 3 seconds longer per rank. | 3 |
| Hundred Hands | Your hands blur: 10% chance per rank to land an extra attack. | 5 |
| Diamond Skin | Conditioning turns aside 5% of all incoming damage per rank. | 4 |
| Iron Body | Breath control raises every resistance by 3% per rank. | 3 |

Turn off `MonkEnabled` in Lunaris Options before starting the game if the class
and its trainers should not be added.

---

## SimCrafting — Whisper-Based Player Crafting Economy

Ask any sim player in the world to craft items for you, just like the old days.

### How It Works

**Requesting a craft:**
Whisper any sim player with a craft request. They'll check their skill, attempt the combine, and quote you a price.

```
/whisper Kaelin craft 5 Copper Rivets
```

The sim rolls success/fail based on their virtual tradeskill level and responds with a price quote. Whisper **yes** to accept or **no** to decline.

**The sim comes to you:**
Once you accept, the sim walks to your location — even if they're across the zone or in a completely different zone. They'll travel to you, walk up, and open a trade window. No courier mail, no instant delivery. They come to you in person.

If the sim is already in your party and standing next to you, the trade opens immediately.

**Accept or Decline the trade:**
A trade confirmation panel appears showing the item, quantity, and gold cost. Click **Accept** to receive the items and pay gold, or **Decline** to pass. If you decline, the sim will ask if you still want it — you can retry up to 3 times before they give up and leave.

### Sim Tradeskill Levels

Each sim has virtual tradeskill levels based on their character level and class:

| Class | Specialty | Max Skill (Lv 35) |
|---|---|---|
| Paladin | Smithing | 180 |
| Windblade / Duelist | Fletching | 180 |
| Reaver | Brewing | 180 |
| Druid | Baking | 180 |
| Arcanist | Jewelcraft | 180 |
| Stormcaller | Tailoring | 180 |

Non-specialty tradeskills cap at 30% of their specialty level. A level 35 Paladin has Smithing 180 but only Baking 54.

### Recipe Knowledge

Not every sim knows every recipe. Basic recipes (trivial ≤ 40) are universal, but higher-tier recipes get progressively rarer:

- Trivial 41–80: 90% chance a sim knows it
- Trivial 81–120: 70%
- Trivial 121–150: 40%
- Trivial 151–165: 20%
- Trivial 166–180: 10%

Recipe knowledge is **deterministic per sim** — the same sim always knows the same recipes. Discover which sims know endgame recipes and seek them out.

### Teaching Sims New Recipes

Find a sim that doesn't know a recipe you need? **Trade them the recipe scroll.** Drag a recipe scroll onto a sim to open the game's native trade window and click Give Items:

- **Sim already knows it** → they decline and hand it back
- **Wrong class specialty** → they explain it's not their tradeskill and return it
- **Success** → the scroll is consumed and the sim permanently learns the recipe

Taught recipes persist across sessions in `sim_recipes.json`. Build up your favorite crafter sims over time.

### Guild Discount

Sims in the same guild as you give a **20% discount** on crafting fees. The price quote will mention "Guild discount applied!" when active.

### Fail Cooldown

If a sim fails every combine attempt, they go on a cooldown (default 60 seconds) for that specific recipe — they'll tell you they're out of materials. You can ask a different sim, or wait for the cooldown to expire. Configurable via `SimCraftFailCooldownSeconds`.

### Ambient World Chat

Sim players now talk about tradeskills, custom items, and skills in chat, making the world feel alive:

- **/say** — Sims chat about crafting, foraging, fishing, weapon skills, and specific items from the mod
- **/shout** — WTS/WTB messages referencing actual crafted items and materials
- **/guild** — Crafting tips, cross-trade advice, and skill leveling reminders for guildmates

Chat frequency is configurable (default: one message every 3–8 minutes).

### Config Options

All SimCraft features are under `[Skill Toggles]` in the mod's Lunaris Options:

| Setting | Default | Description |
|---|---|---|
| `EnableSimCraft` | `true` | Toggle the entire SimCraft whisper system on/off |
| `SimCraftFailCooldownSeconds` | `60` | Cooldown after a sim fails all combines for a recipe |
| `SimChatIntervalMinSeconds` | `180` | Min seconds between ambient tradeskill chat |
| `SimChatIntervalMaxSeconds` | `480` | Max seconds between ambient tradeskill chat |

---


### Material Economy

Materials come from gameplay — no vendor shortcuts for valuable resources.

**Mining Nodes** drop bonus materials based on zone tier (60% chance per mine). Player level gating prevents low-level players from farming high-tier zones:

| Zone Tier | Zones | Req. Level | Common Ore | Rare Ore/Gem | Refined Metal | Endgame |
|---|---|---|---|---|---|---|
| T1 | Stowaway, Tutorial | 1 | Copper Ore | Coal, Smooth Pebble | — | — |
| T2 | Fernalla, Hidden, Brake | 5 | Copper/Iron Ore | Silver Ore, Rough Geode | — | — |
| T3 | Azure, Windwashed, Silkengrass | 12 | Iron Ore, Coal | Silver, Ruby, Emerald | Void Iron | — |
| T4 | Braxonian, Elderstone, Malaroth | 18 | Silver/Iron Ore | Gold, Sapphire, Bloodstone | Void Iron, Soulsteel | — |
| T5 | Soluna, Ripper, Bonepits, Vitheo | 25 | Adamantite, Platinum | Diamond, Opal, Starshard | Soulsteel, Moonsilver, Sunforged | Worldtree Splinter |
| T6 | Abyssal, Azynthi, PrielPlateau | 30 | Adamantite, Gold | Prismatic Jewel, Diamond | Starmetal, Moonsilver, Sunforged | Crystallized Time, Essence of Eternity |

**Monster Drops** (independent rolls, appear in loot window):

| Material | Monster Type | Min Level | Drop % |
|---|---|---|---|
| Beast Meat | Beasts (wolf, bear, boar, etc.) | Any | 25% |
| Worn Leather Scrap | Beasts | 1–17 | 20% |
| Tough Hide Strip | Beasts | 18+ | 20% |
| Spider Silk Strand | Spiders | 1–14 | 35% |
| Abyssal Silk Thread | Spiders | 15+ | 35% |
| Celestial Silk Bolt | Spiders | 25+ | ~7% |
| Beast Bone Shard | Wyrms, Skeletons | Any | 25–30% |
| Elder Wyrm Bone | Wyrms, Dragons | Any | 15–30% |
| Phoenix Feather | Fire creatures | 15+ | 20% |
| Breath of the Abyss | Void/Blight/Undead | 15+ | 20% |
| Malaroth Scale Fragment | Malaroths | Any | 35% |
| Frostweave Strand | Frost/Ice creatures | 15+ | 25% |
| Titan's Knucklebone | Giants, Golems | 20+ | 15% |

**Endgame Boss Drops** (level 25+ mobs):

| Material | Boss (20%) | Non-Boss (5%) |
|---|---|---|
| Thread of Fate | Bosses 30+ | — |
| Essence of Eternity | Bosses 30+ | Level 30+ mobs |
| Crystallized Time | Bosses 25+ | Level 25+ mobs |
| Worldtree Splinter | — | Level 25+ mobs |

**Fishing** (non-junk catches):
- Raw Fish — every catch (used in 9 Baking recipes)
- Moongill Trout — rare moongill catches
- Golden Carp — rare golden fish catches

**Vendor and Foraging** (basic crafting supplies):
- Herbs, wheat, water, berries, mushrooms, peppers
- Basic components (tangled string, enchanted root, faerie dust, etc.)
- No ores, gems, metals, monster parts, or endgame materials on vendors

---

### Chat Commands

| Command | Description |
|---|---|
| `/skills` | Overview of all skill levels (existing chat command) |
| `/erenshorclassicskills skills` | Native Lunaris command for the skill overview |
| `/erenshorclassicskills extrasettings` | Open the chat and combat-log settings |
| `/smith list`, `/bake list`, etc. | List known recipes |
| `/meditate` | Explain automatic meditation requirements |

### Optional test mode

Test mode is disabled by default. It provides item grants, skill controls, visual checks and class/combat probes for backed-up test characters. See the [complete in-game testing command reference](docs/IN_GAME_TESTING.md); keep it disabled during ordinary play.

### Default Keybinds

| Key | Action |
|---|---|
| F8 | Open/Close Skills window |
| F9 | Forage |
| F10 | Bind Wound |
| ; | Beg |

Forage, Bind Wound, and Begging also appear as 42x42 icons beside the Skills-menu
icon. Each shows its configured key in the corner. While an action is cooling
down, the remaining seconds appear directly over its icon. Drag any utility icon
to move the three-icon group; its position is saved across launches. Set
`ShowUtilityActionButtons=false` in the config if you prefer hotkeys only. An
icon is hidden automatically when its skill is disabled.

---

## Configuration

Open the Lunaris plugin manager, select **EverShor - Classic Skills, Classes and Features**, and use
**Options**. Use the in-game Options controls to adjust settings; Lunaris saves them for you.

Key settings:
- `SkillWindowKey` — Keybind to open Skills window (default: F8)
- `ShowUtilityActionButtons` — Show the draggable Forage/Bind Wound/Begging HUD icons (default: true)
- `EnableCombatSkills` — Toggle combat weapon skills on/off
- `EnableMagicSkills` — Toggle magic school skills on/off
- `CombatSkillDamagePerLevel` — Damage bonus per combat skill level (default: 0.085)
- `EvocationDamagePerLevel` — Spell damage multiplier increment per Evocation level (default: 0.0008, or 0.08%)
- `AbjurationDurationPerLevel` — Buff-duration multiplier increment per Abjuration level (default: 0.003, or 0.3%)
- `ConjurationDamagePerLevel` — DoT and summoned-pet damage multiplier increment per Conjuration level (default: 0.0008, or 0.08%)
- **Extra Settings** — Change each chat/combat-log category color and toggle resolved damage-type labels in game
- `CorpseRunEnabled` — Leave a recoverable character corpse and protected Corpse Bag on death (default: false)
- `NecromancerEnabled` — Add the playable Necromancer, spells, armed Bone Thralls, and city spell merchants (default: true)
- `MonkEnabled` — Add the playable Monk, animated martial techniques, auras, class gear, and city trainers (default: true)
- `EnableTestMode` — Enable controlled `/cstest` commands (default: false)
- Individual toggles for Fishing, Swimming, Meditate, Foraging, etc.

---



## SimPlayer skills and custom classes

Sim players receive virtual combat/magic skill bonuses based on their level and class. Melee classes favor weapon skills; casters favor magic schools. `EnableSimPlayerSkills` controls these bonuses. Some Sims can also be assigned the custom Necromancer or Monk class, with corresponding combat abilities, party roles and class chatter. Monk Sim combat is an area of ongoing compatibility work.

### Catch-up for existing characters

Use `CatchUpCombatMagicSkills` and/or `CatchUpUtilityTradeskills` to initialize appropriate progression for an existing character. `ForceCatchUpOnNextLoad` requests a one-time reapplication on the next load and then resets. These options are separate from training through normal gameplay. Native class tradeskill affinities favor Paladin/Smithing, Windblade/Fletching, Reaver/Brewing, Druid/Baking, Arcanist/Jewelcraft and Stormcaller/Tailoring.

### Buying and selling with Sims

Ambient **WTB** and **WTS** messages refer to real mod materials and craftable items. With `EnableSimWTB`, whisper a Sim about an item they have requested to arrange a sale. With `EnableSimWTS`, ask about a Sim's advertised crafted item to arrange a purchase. Offers expire according to the configured WTB/WTS expiry; a past chat message is not a permanent shop listing. Ordinary native trading and item/class restrictions still apply.

## Thrall equipment and combat in detail

The Necromancer's Bone Thrall has its own character-style equipment window, accessible through the Thrall equipment/Bag interaction. It is not a second player character's backpack: slots represent equipped gear.

| Equipment group | Slots |
|---|---|
| Armor and accessories | Head, Neck, Chest, Arm, Hand, Leg, Waist, Foot, Back |
| Paired accessories | Two Bracers and two Rings |
| Weapons | Primary and Secondary |
| Special equipment | Aura and Charm |

The window provides **17 equipment slots** and the Thrall's live statistics. Items must match the destination slot. A second chest piece cannot occupy a different armor slot; paired bracers and rings each have their own position. Thrall gear handling allows equipment from different player classes without removing the player character's own class restrictions.

Equipment contributes health, mana, armor, attributes and resistances. Weapons contribute their damage and attack behavior; melee damage benefits from Strength and ranged damage from Dexterity. Item quality is taken into account. Equipping gear is intended to affect combat, not only the inventory display. Armor/weapon models use the skeleton's compatible visual mapping; some combinations still require visual testing.

Thralls retain their own identity: they **do not copy the Necromancer's learned skills or spells**. Their combat kit combines the summon tier, equipped item abilities and supported equipment procs. Weapon/armor on-hit procs are checked using the equipped item's proc information; item-granted spells/skills remain subject to the supported AI ability types and level checks. Equipping an item does not guarantee the AI can use every player-only utility attached to it.

### Summon tiers

Summoning progression replaces the active Bone Thrall with a stronger tier; this is not a multi-summon army system.

| Level | Summon | Exclusive attack |
|---:|---|---|
| 1 | Cavorting Bones | Bone Rake |
| 8 | Risen Sentry | Sentry's Rebuke |
| 18 | Gravebound Soldier | Grave Siphon, including a lifetap |
| 24 | Crypt Vanguard | Crypt Breaker |
| 32 | Ossuary Champion | Ossuary Verdict |

| Technique | Availability | Cooldown | Purpose |
|---|---|---:|---|
| Tier attack | All tiers | 6 s | Supplemental damage alongside equipped weapons |
| Grave Challenge | All tiers | 1.25 s | Direct taunt and additional threat |
| Area Grave Challenge | All tiers | 2 s | Retake nearby engaged enemies within 12 m |
| Grasp of the Grave | Tier 2 onward | 11 s | Reduce enemy movement and attack speed |
| Crypt Bash | Tier 3 onward | 15 s | Brief stun |
| Ossuary Hammer | Tier 5 | 15 s | Final-tier version of the stun |

The area taunt only considers NPCs already fighting the owner or Thrall. Nearby neutral NPCs, dead targets, charmed targets and NeverAggro NPCs are excluded. Control duration, resistance/immunity and practical ability frequency depend on native combat and AI behavior; cooldowns alone do not guarantee a cast at every interval.

## Chat, interface and attack timing

**Extra Settings** lets you customize 23 chat/combat-log color categories and optionally show damage-school labels. The Skills window lists active bonuses, while the draggable utility buttons show Forage, Bind Wound and Begging cooldowns. Normal game tooltips continue to show item and weapon-skill information.

The integrated **Attack Sync Fix** adjusts hit timing to match attack animations. Player, NPC and Sim attack synchronization have separate toggles. Turning off **Sync Player Attacks** also opts Monk handwrap attacks out of the replacement player attack path, which is useful when diagnosing animation or interaction conflicts.

**Item Compare is a separate mod.** It is not part of this Classic Skills DLL. The spell-cast Diagnostics plugin is also a separate troubleshooting tool, not a gameplay feature required by Classic Skills.

## Local 1.9.2 preview: Pummel messages

A nearby Sim Monk can produce Pummel follow-up hits even if your character has never learned Pummel. The 1.9.2 preview identifies the real caster in the combat log and isolates follow-up eligibility from the local player class. See the [report and tester check](docs/MONK-PUMMEL-REPORT.md). Preview 2 also fixes a Sim Resonant Palm cast enabling the player's auto-attack: only the local caster may auto-engage, and the native preference is respected. See the [guild-menu auto-attack report](docs/MONK-AUTOATTACK-REPORT.md). These previews are evaluated separately from the 1.9.1 PR.

## What changed in 1.9.1

This release builds on 1.9.0, already merged upstream, and includes the Monk corrections currently under community testing.

- The active character class takes priority over stale saved-slot labels, and non-Monk actors cannot execute custom Monk techniques.
- Sim Monks use their own combat targets and the NPC skill path. Their casts do not modify player hotkey cooldowns or award technique training to a different character.
- The mod now validates and animates both native skill execution paths, including the no-check path used by NPCs. Offensive techniques check caster/target life, reach and the Sim's current aggro target.
- Sim utility techniques that rely on the player's stance timers are excluded, and null skill/spell lookups are handled safely.
- This full gameplay guide restores the skill, class, crafting, SimCraft and economy explanations and updates the progressions against source.

Some improvements have been reported by community testers. Sim approach/positioning, animation compatibility and the exact origin of cross-class spellbook contamination remain under investigation. See [1.9.1 release notes](ErenshorClassicSkills/ErenshorClassicSkills-v1.9.1-Changes.md). Additional fixes will receive subsequent versions.

## What changed in 1.9.0

- **Native Lunaris support:** settings are adjusted in Lunaris Options, with native commands for the skill overview and Extra Settings. Old JSON data discovery is retained for migration.
- **A dedicated class page:** Monk and Necromancer move off the native class grid, with optional Fate of the Fallen page coordination.
- **Expanded Thrall equipment:** character-style slots, Aura/Charm support, gear-derived attributes, weapon damage and guarded equipment visuals.
- **Thrall-owned combat:** tier attacks, equipment abilities/procs and rapid direct/area taunts replace copying the player's spellbook. Slow and stun add control as the summon advances.
- **Necromancer spell fixes:** Venomous Blood receives corrected icon handling, and offensive spells use native auto-attack activation.
- **Monk compatibility:** player attack-sync opt-out applies to handwraps. Grandmaster's Presence can also be used by Reaver. Version 1.9.1 adds the actor-isolation corrections described above.
- **Cleanup and stability work:** release owned UI/spell resources, reduce repeated area-taunt allocations, simplify chat-color bindings and restrict the periodic-effect exception mitigation to custom Thralls.

These are implemented systems with ongoing runtime validation, not a promise that every UI combination, Sim animation or Unity crash is resolved. See [known issues and validation](docs/VALIDATION.md) and the [changelog](CHANGELOG.md) for the exact review scope.

## For contributors

Players do not need to build the mod. Developers can use the [build guide](docs/BUILDING.md), [change inventory](docs/CHANGE_INVENTORY.md) and [migration audit](ErenshorClassicSkills/NATIVE-LUNARIS-MIGRATION-AUDIT.md). These technical documents are separate from this gameplay overview.

## Credits

- **Original mod credit:** Sovietpremier (Thurderan), as listed in the upstream feature README.
- **Upstream repository:** KrautchanPro's EverShor - Classic Skills, Classes and Features.
- **Game:** Erenshor by Burgee Media.
- **Runtime and patching:** Lunaris and Harmony.
- **Artwork/audio/model credits:** [third-party notices](ErenshorClassicSkills/THIRD_PARTY_NOTICES.md).

Original license files and notices are retained. See the repository's root LICENSE and nested LICENSE.txt for the texts already supplied by upstream.
