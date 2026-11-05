# In-game testing commands

### Controlled Test Mode

Test mode is disabled by default. Enable `EnableTestMode` from Classic Skills'
**Options** page in Lunaris. Restart Erenshor and use it only on a backed-up test
character. Disable it again for normal play.

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
| `/cstest simclasses` | Validate persistent Necromancer and Monk SimPlayers, native save fallbacks, combat kits, roles, and class chatter |
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
