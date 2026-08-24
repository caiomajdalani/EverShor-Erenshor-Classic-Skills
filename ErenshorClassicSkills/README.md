# Evershor - Classic Skills

An EverQuest-inspired skills and tradeskills mod for [Erenshor](https://store.steampowered.com/app/2382520/Erenshor/). Adds 25 trackable skills, 6 tradeskills with 280 recipes, 4 magic schools, a full material economy, and balanced playable Necromancer and Monk classes.

## Compatibility

Version **1.8.0** is built and tested for Erenshor Steam build **24405256**
(installed July 25, 2026). It supports either **BepInEx 5** or Lunaris through Lunaris's
legacy-plugin wrapper, including the current auto-updated Lunaris 0.1.9 runtime.
Attack Sync Fix is included; do **not** install the standalone Attack Sync Fix
DLL at the same time.

## Developer MCP server

The local stdio server in [`mcp-server/`](mcp-server/) supports the complete
agent development loop: installed-game API discovery, build/reference refresh,
guarded BepInEx or Lunaris installation, Steam launch, `/cstest` queueing, and
report/log/screenshot inspection. See [`MCP_SERVER.md`](MCP_SERVER.md) for the
project review and architecture, then [`mcp-server/README.md`](mcp-server/README.md)
for setup and Codex registration.

## Installation

Choose **one** loader. Do not run BepInEx and Lunaris bootstraps together.

### BepInEx 5

1. **Install BepInEx 5** for Erenshor if you haven't already:
   - Download [BepInEx 5.x](https://github.com/BepInEx/BepInEx/releases) (Unity Mono version)
   - Extract into your Erenshor game folder (`Steam/steamapps/common/Erenshor/`)
   - Run the game once to generate BepInEx folders, then close it

2. **Install ErenshorClassicSkills:**
   - Extract the zip into your Erenshor game folder
   - Your folder structure should look like:
     ```
     Erenshor/
     └── BepInEx/
         └── plugins/
             ├── ErenshorClassicSkills.dll
             └── ClassicSkills/
                 └── Icons/
     ```
   - Icons and station models are embedded in the DLL. The packaged icon
     directory also replaces older external overrides during an upgrade.

3. **Launch the game.** The mod generates its config on first run.

### Lunaris

1. Install and launch [Lunaris](https://github.com/MizukiBelhi/Lunaris) once.
2. Remove any older Classic Skills DLL from `Erenshor/plugins/`. Extract the
   Lunaris ZIP into the Erenshor game directory so it installs
   `plugins/ErenshorClassicSkills.dll` and `plugins/ClassicSkills/Icons/`.
   A standalone DLL also works on a fresh installation; when upgrading an old
   external icon pack, use the ZIP so the new production icons replace it.
3. Make sure there is only one copy of Classic Skills installed. Do not also
   leave it in `BepInEx/plugins/`, and do not install the standalone Attack Sync
   Fix DLL because that fix is already included.
4. At the main menu, open Lunaris's plugin installer, or use
   `/lunaris plugins` in game, and enable **Erenshor Classic Skills**.
5. Restart Erenshor once after enabling the addon, then enter a character.

Manually installed Lunaris plugins begin disabled. Enable Classic Skills before
entering a character whenever possible so Erenshor resolves its `CS_` item IDs
during the native load. If it is first enabled while a character is already in
the world, the addon performs a guarded recovery using the last valid native
backup plus its inventory sidecar. A fresh Lunaris install stores addon data in
`Erenshor/plugins/config/ClassicSkills/`; BepInEx uses
`Erenshor/BepInEx/config/ClassicSkills/`. When changing loaders, Classic Skills
automatically follows whichever of those locations already contains its durable
JSON data so skill, recipe, bag, and inventory recovery data are not split.

---

## Features

### Skills System (25 Skills)

Press **F8** (configurable) to open the Skills window. Skills level up through use via an EQ-style skill-up chance system with a trivial cap. Training Points (3 per player level) can be spent to manually raise skills. Skill cap formula: `(PlayerLevel × 5) + 5` — max 180 at level 35.

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
- **Starter recipes** (2 per tradeskill) — auto-learned on first load
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

Corpse Run is disabled by default. Enable it with `/corpserun on`, BepInEx's
`Corpse Run.Enabled=true`, or Lunaris's `CorpseRunEnabled` option to leave your carried inventory, equipped gear,
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

Necromancer is enabled by default and appears alongside the native classes in
character creation. Its 52-point starting-stat budget and 35-spell progression
match the current native class envelopes rather than adding an overpowered
bonus class.

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
- Bone Thralls use Erenshor's skeletal companion model. Weapons and shields can
  be handed to a thrall through the native trade window and visibly appear in
  the correct hands while contributing their real damage, armor, stats, and
  upgrade quality. Bow-equipped thralls hold ranged distance, fire native arrow
  projectiles, and visibly draw and release instead of closing to melee range.
- Bone Thralls prefer Erenshor's bare `Summoned Skeleton` model and use a
  level-1–35 stat curve benchmarked below comparable native Druid pets. Gear
  contributions are tier-bounded so heavily upgraded items cannot make a
  low-tier thrall overpower native companions. Their navigation speed follows
  the owner's effective movement speed instead of using the native nearby-pet
  half-speed behavior.
- Pet equipment is held in crash-safe character escrow. Exact item identity,
  raw quality, and mod-added scalar state survive zoning, dismissal, death,
  restart, and resummoning. A full inventory blocks a return without deleting
  or duplicating either item.
- Spell merchants in Stowaway's Step, Willowwatch, and Port Azure—Ossira the
  Bone Reader, Morthane the Grave Tutor, and Veyra of the Azure Crypt—sell the
  level-sorted vendor portion of the kit plus all five auras. Eight high-level
  scrolls instead drop from 24 named/boss sources, including three Planar raid
  drops, so the class participates in native spell acquisition progression.
  Each Necromancer trainer is anchored beside that city's native Arcanist
  trainer rather than an unrelated merchant. Like the Monk trainers, these
  merchants never stock a weapon or armor piece above level 10; charms, auras,
  and scrolls remain purchasable at every tier.
- Five native Ascension perks are available on the Necromancer class tab:
  Grave Mastery, Virulent Afflictions, Bone Sovereignty, Unyielding Affliction,
  and Swift Decay. Their ranks use Erenshor's native save and effect paths.
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

Set BepInEx's `Necromancer.Enabled=false` or turn off Lunaris's
`NecromancerEnabled` option before starting the game if the class and its vendors
should not be added.

### Playable Monk

Monk is enabled by default and appears beside the native classes in character
creation. Its starting-stat budget, native resource use, equipment restrictions,
and level 1-35 output were built against the same native class envelopes used for
Necromancer rather than modifying any base class.

- 32 uniquely named Monk techniques plus the seven native martial abilities
  shared with other melee classes, for a 39-ability level 1-35 progression.
- Twenty-eight active custom techniques are native spellbook entries with
  native damage or beneficial spell classification. Every active Monk spell
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
- Five Monk auras mirror native Duelist aura budgets exactly: Stillwater Breath,
  Flowing Reed, Iron Body, Path of the Gale, and Grandmaster's Presence.
- Five persistent native Ascension perks are available on the Monk class tab:
  Way of Iron, Deep Mend, Flowing Defense, Lingering Flurry, and Perfect
  Stillness. Way of Iron adds 15% Monk technique damage per rank.
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
- Comparative live testing completed 576/576 parses at levels 5, 10, 15, 20,
  25, 30, and 35 across budget, progression, and optimized class-appropriate
  gear, followed by 39/39 focused verification parses and 24/24 final balance
  guards. Raid-drop spells were excluded and no native class value was changed.

Set BepInEx's `Monk.Enabled=false` or turn off Lunaris's `MonkEnabled` option
before starting the game if the class and its trainers should not be added.

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
| `/skills` | Overview of all skill levels |
| `/smith list`, `/bake list`, etc. | List known recipes |
| `/meditate` | Explain automatic meditation requirements |

### Controlled Test Mode

Test mode is disabled by default. With BepInEx, set
`Testing.EnableTestMode=true` in
`BepInEx/config/com.erenshor.classicskills.cfg`. With Lunaris, enable
`EnableTestMode` from Classic Skills' **Options** page. Restart Erenshor and use
it only on a backed-up test character. Disable it again for normal play.

| Command | Description |
|---|---|
| `/cstest find <text>` | Search addon and native item names |
| `/cstest item <name> [qty]` | Grant an exact item through a saved transaction |
| `/cstest quality <slot> <value>` | Set and persist a raw quality value on non-stackable test gear |
| `/cstest mats <recipe>` | Grant one combine's required materials |
| `/cstest skill <name\|all> <0-180>` | Set controlled test skill levels |
| `/cstest skillprogress <name> <0-100>` | Set exact within-level progress for clipped-bar visual testing |
| `/cstest cooldowns <on\|off>` | Toggle one-second utility test cooldowns |
| `/cstest open <skills\|tradeskill>` | Open each addon window directly |
| `/cstest tradeui <tradeskill> <top\|bottom>` | Select a real recipe row and exercise both scrollbar boundaries without learning recipes |
| `/cstest vendor <stock\|recipes>` | Open the native merchant at the stock/recipe boundary and validate recipes-last ordering |
| `/cstest smoke` | Exercise every addon item through native inventory and bag round-trips, validate every recipe and feature hook, and write a detailed report |
| `/cstest gear [filter]` | Equip matching gear through native slots and capture labeled front/back visual evidence |
| `/cstest corpse <die\|redie\|status>` | Exercise the real death path and outstanding-corpse safeguard |
| `/cstest capacity <fill\|clear\|status>` | Test full-inventory recovery without mutating real belongings |
| `/cstest pet <summon\|probe\|attack\|status>` | Verify Conjuration scaling on player-owned pets |
| `/cstest logs` | Capture the native menu buttons, Extra Settings pages, all configured log colors, and damage-type labels |
| `/cstest icons` | Validate and capture all 280 crafted output icons in six labeled in-game catalogs |
| `/cstest simitems` | Reversibly live-test SimPlayer crafted armor, offhand gear, Food, Drink, save IDs, and quantities |
| `/cstest necro <status\|class\|level\|learn\|cast\|castall\|vfx\|icons\|aura\|gear\|ascension\|petgear\|petrole\|epic\|travel\|vendor>` | Validate Necromancer creation, level-1–35 balance, spells, VFX, acquisition, Ascensions, gear, pets, and merchants on John EverQuest |
| `/cstest monk <status\|class\|levels\|learn\|icons\|vfx\|motions\|mechanics\|auras\|gear\|ascensions\|vendor>` | Validate Monk creation, level-1–35 progression, unique art, contact-timed VFX, all body motions, native damage mechanics, auras, new gear, Ascensions, and trainers on John EverQuest |
| `/cstest monk autoattack [side]` | Exercise repeated primary-weapon attacks and verify the matched two-hand punch sequence without weapon-state leaks |
| `/cstest monk dps <full\|monk-retune>` | Run the full multi-level, multi-gear comparative audit or its focused post-tuning matrix |
| `/cstest uidump` | Write native UI hierarchy evidence for visual compatibility work |
| `/cstest status` | Show runner status and the latest report path |

Smoke reports are written under `ClassicSkills/TestReports/` inside the active
addon-data root described in the installation section. Gear screenshots and a
per-item manifest are written under its `GearVisuals/` subfolder; crafted icon
catalogs are written under `CraftedItemIcons/`. The runner checks declared/equipped slots, native model activation,
weapon type/damage/range, material palettes, all recipes, loader lifecycle paths,
and inventory/bag round-trips. Temporary smoke probes and equipment are restored
in memory and are not saved; explicit item/material grants are persisted and
should be used only with snapshotted saves.

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

BepInEx settings use
`BepInEx/config/com.erenshor.classicskills.cfg`. Under Lunaris, open the plugin
installer, select **Erenshor Classic Skills**, and use **Options**; Lunaris backs
those settings with `plugins/config/erenshorclassicskills.lpcfg`, which is not a
text file and should not be edited by hand.

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
- `Corpse Run.Enabled` / Lunaris `CorpseRunEnabled` — Leave a recoverable character corpse and protected Corpse Bag on death (default: false)
- `Necromancer.Enabled` / Lunaris `NecromancerEnabled` — Add the playable Necromancer, spells, armed Bone Thralls, and city spell merchants (default: true)
- `Monk.Enabled` / Lunaris `MonkEnabled` — Add the playable Monk, animated martial techniques, auras, class gear, and city trainers (default: true)
- `EnableTestMode` — Enable controlled `/cstest` commands (default: false)
- Individual toggles for Fishing, Swimming, Meditate, Foraging, etc.

---

## Credits

- **Mod by**: Sovietpremier(Thurderan)
- **Built with**: BepInEx 5 legacy API and HarmonyX; tested with BepInEx and Lunaris
- **For**: [Erenshor](https://store.steampowered.com/app/2382520/Erenshor/) by Burgee Media
