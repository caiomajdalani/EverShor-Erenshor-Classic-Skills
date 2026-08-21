# Evershor - Classic Skills and Features

An EverQuest-inspired skills, classes and tradeskills mod for [Erenshor](https://store.steampowered.com/app/2382520/Erenshor/). Adds 25 trackable skills, 2 new classes, 7 tradeskills with 280 recipes, 4 magic schools, a full material economy driven by mining, fishing, hunting, and boss kills.

## Installation

### IMPORTANT: DOWNLOAD THE LATEST VERSION OF THE ADDON FROM DISCORD https://discord.com/channels/1099145747364057118/1487583970370519071/1538004888250884226

1. **Install BepInEx 5** for Erenshor if you haven't already:
   - Download [BepInEx 5.x](https://github.com/BepInEx/BepInEx/releases) (Unity Mono version)
   - Extract into your Erenshor game folder (`Steam/steamapps/common/Erenshor/`)
   - Run the game once to generate BepInEx folders, then close it

2. **Install evershor-classic-skills-and-features:**
   - Extract the zip. Place the ErenshorClassicSkills.dll in your plugins folder. 
   - Your folder structure should look EXACTLY like below:
     ```
     Erenshor/
     └── BepInEx/
         └── plugins/
             ├── ErenshorClassicSkills.dll
     ```
OR if you use Lunaris:

3. In your Erenshor folder, drop the dll in the plugins folder


4. **Launch the game.** The mod generates its config on first run.

---

## Features

### Two new classes; NECROMANCER AND MONK
Necromancer and Monk are selectable at the character creation screen

They have spells, skills, craftable gear, and end game gear advancement just like the native classes

### Skills System (25 Skills)

Press **F8** (configurable) to open the Skills window. Skills level up through use and via an EQ-style skill-up chance system with a trivial cap. Training Points (3 per player level) can be spent to manually raise skills. Skills will not start leveling up till you put a training point in it. Skill cap formula: `(PlayerLevel × 5) + 5` — max 180 at level 35.

**Utility Skills (6):**
- **Foraging** — Gather herbs and materials from the wild (F9 keybind)
- **Swimming** — Levels from swimming; increases swim speed (+0.3% per level)
- **Bind Wound** — Heal yourself out of combat (+0.15% max HP per level, F10 keybind)
- **Meditate** — Regenerate mana while sitting; works in combat (M keybind)
- **Food Tolerance** — Extends food/drink buff duration (+0.5% per level)
- **Begging** — Ask NPCs for gold, items, or lore (; keybind)

**Combat Skills (8):**
- 1H Slashing, 1H Blunt, Piercing, 2H Slashing, 2H Blunt, Archery, Wands, Hand to Hand
- Each weapon skill adds bonus damage: +0.085% per level (up to +15.3% at 180)
- Weapon skill type shown on all weapon tooltips

**Magic Skills (4):**
- **Evocation** — Levels on damage spells. +0.08% spell damage per level
- **Abjuration** — Levels on buff spells. +0.3% buff duration per level
- **Alteration** — Levels on heals. +0.08% heal amount per level
- **Conjuration** — Levels on DoTs. +0.08% DoT damage per level

**Tradeskills (7):**
- Smithing (52 recipes), Baking (55), Brewing (46), Fletching (29), Jewelcraft (47), Tailoring (51), Fishing(increases nibble chance the higher the skill is)
- 280 total recipes crafted at stations placed in Port Azure and Stowaway's Step
- Can also use fires, ovens, workbenches, brewing barrels, and Forges throughout the game for Baking, Brewing, Smithing, and Fletching
- Right click recipes you find or purchase to learn them

### Simplayer Skills
- Simplayers also receive the same skill bonuses the player does. 
- Simplayer skill bonuses is simulated, they receive bonuses based on their level and class
- Melee oriented classes receive more of a bonus to Combat Skills, Magic oriented classes receive more of a bonus to their Magic Skills
- This can be turned off in the config file at line EnableSimPlayerSkills, default is EnableSimPlayerSkills = true , set to false to turn it off

### Catchup Mechanic
- In the config file are 3 options: 
- CatchUpCombatMagicSkills = false
- CatchUpUtilityTradeskills = false
- ForceCatchUpOnNextLoad = false
- Set them all to true if you want your character to start off with skill levels appropriate for their class and level. 
- Class tradeskill starting bonus: Paladin=Smithing, Windblade=Fletching, Reaver=Brewing, Druid=Baking, Arcanist=Jewelcraft, Stormcaller=Tailoring

---

### Tradeskill Crafting

Craft at appropriate stations. There are custom stations(loom, brewing barrel, oven, jewlers kit, fletching table) for each tradeskill at Port Azure and the castle/Warehouse(in the room to the right) on stowaway island. Campfires and ovens also work for baking. Brew barrels also work for brewing. Workbenches across the game also work for Fletching. Forges work for Smithing. Crafting has a success/fail chance based on skill level vs recipe difficulty. Failed combines consume materials (just like EQ).

**Recipe Sources:**
- **Starter recipes** (2 per tradeskill) — auto-learned on first load
- **Vendor recipes** (skill 1–90) — bought as scrolls from merchants
- **Drop-only recipes** (skill 90+) — found in mob loot windows (5% normal, 30% boss)
- **Boss-only recipes** — drop exclusively from named/boss mobs
- Right-click recipe scrolls in inventory to learn them

**Crafting Stations:**
- Custom 3D stations spawned in Port Azure and Stowaway's Step
- Game objects (campfires, stoves, ovens, barrels, workbenches) also work as stations
- The game's existing Forge works for Smithing

**Craftable Equipment (153 pieces):**
- Smithing: Weapons and shields (ilvl 2–24)
- Fletching: Bows (ilvl 1–14)
- Jewelcraft: Rings, necklaces, headpieces, charms (ilvl 1–25)
- Tailoring: Cloth/leather armor, cloaks (ilvl 2–25)

**Craftable Consumables (55 items):**
- Baking: Food buffs scaling from +2 stats to +15 all stats
- Brewing: Drink buffs on the same curve
- Food/Drink use a category system — one Food buff and one Drink buff active at a time

**Bags:**
- Silken Pouch (4 slots, Tailoring 5)
- Herbalist's Satchel (6 slots, Tailoring 10)
- Simple Backpack (8 slots, Tailoring 25)
- Adventurer's Backpack (10 slots, Tailoring 50)

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
| Duelist | Fletching | 180 |
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

All SimCraft features are under `[Skill Toggles]` in the BepInEx config:

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
| `/skills` | Overview of all skill levels |
| `/smith list`, `/bake list`, etc. | List known recipes |
| `/meditate` | Toggle meditation |

### Default Keybinds

| Key | Action |
|---|---|
| F8 | Open/Close Skills window |
| F9 | Forage |
| F10 | Bind Wound |
| M | Toggle Meditate |
| ; | Beg |

---

## Configuration

Config file: `BepInEx/config/com.erenshor.classicskills.cfg` (generated on first run)

Key settings:
- `SkillWindowKey` — Keybind to open Skills window (default: F8)
- `EnableCombatSkills` — Toggle combat weapon skills on/off
- `EnableMagicSkills` — Toggle magic school skills on/off
- `CombatSkillDamagePerLevel` — Damage bonus per combat skill level (default: 0.085)
- `EvocationDamagePerLevel` — Spell damage bonus per Evocation level (default: 0.08)
- `AbjurationDurationPerLevel` — Buff duration bonus per Abjuration level (default: 0.3)
- Individual toggles for Fishing, Swimming, Meditate, Foraging, etc.
- At the bottom of the config file is a section called [Tradeskills], it contains the options CraftFromBags = false and CraftFromBank = false, set both to true if you want to be able to craft using materials from the bags or bank

---

## Credits

- **Mod by**: Sovietpremier(Thurderan)
- **Built with**: BepInEx 5, HarmonyX
- **For**: [Erenshor](https://store.steampowered.com/app/2382520/Erenshor/) by Burgee Media
