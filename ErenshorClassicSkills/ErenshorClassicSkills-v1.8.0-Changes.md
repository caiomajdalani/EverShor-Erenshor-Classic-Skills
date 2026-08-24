# Erenshor Classic Skills v1.8.0

## Class-trainer and full-hand wrap hotfix

- Monk trainers now wait for and spawn beside the native Reaver trainer in
  each supported city. Necromancer trainers do the same beside the native
  Arcanist trainer; neither class silently falls back to an unrelated merchant.
- Handwrap weapons now combine the skinned wrist wrap with the native
  fingerless hand layer, covering the back of the hand and following the hand
  and finger bones throughout attacks and movement.
- Trainer validation records the required class anchor, distance, placement,
  and native source. Test mode can capture both trainers together for visual
  regression evidence.

## Visible Monk handwrap hotfix

- Equipped handwrap weapons now display on the player model. Each weapon drives
  an independently skinned native Palmwraps overlay on its matching hand, so
  main-hand-only, off-hand-only, and dual-wield configurations remain accurate.
- The overlays follow the active player skeleton through idle, movement,
  techniques, and alternating main/off-hand autoattacks without restoring the
  generic held-weapon model or sword-swing animation.
- Runtime QA now requires a live renderer for equipped handwraps. The complete
  in-game smoke catalog passed **1401/1401**, and two three-attack camera runs
  passed **18/18 captured frames** with zero hand-sequence, pose, recovery, or
  native double-attack-state failures.

## Necromancer cast and companion hotfix

- Necromancer spell casts no longer clone or display the native caster-surrounding spell effect. The native charge channel is now a silent compatibility placeholder, leaving the bespoke Necromancer cast aura as the only visible caster VFX.
- Necromancer resolve effects are custom-only and no longer carry copied native effect hierarchies. This removes the largest per-cast object and particle multiplier behind reported frame stalls.
- New Necromancers receive both a Rusty Dagger and Rusty Shortsword, allowing one weapon to be placed in the Thrall Bag immediately.
- Bone Thralls synchronize their navigation speed to their owner's effective movement speed every frame, overriding the native companion behavior that slows nearby pets to half speed without bypassing rooted or stopped states.
- Runtime QA now rejects visible/native charge-channel regressions, missing starter weapons, and active Bone Thrall speed mismatches.

## Compatibility

- Built and tested against Erenshor Steam build **24405256**, installed July
  27, 2026.
- Supports BepInEx 5 and Lunaris through Lunaris's legacy BepInEx-plugin
  wrapper. Lunaris's official 0.1.0 bootstrap currently updates to runtime
  0.1.9.
- Fixed a Lunaris 0.1.9 legacy-scanner failure caused by the addon's one
  enum-valued Harmony constructor-patch attribute. The same constructor is now
  resolved through `TargetMethod`, preserving behavior while allowing Lunaris
  to inspect the assembly before it supplies its Harmony compatibility layer.
- Upgrades directly from v1.7.0. Existing skills, recipes, crafted bags,
  inventory recovery data, corpse records, Necromancer spells, and pet-gear
  escrow are unchanged.
- Attack Sync Fix remains integrated. Do not install its standalone DLL beside
  Classic Skills.

## Ship-readiness hardening

- Removed the addon's three persistent fallback EventSystems. Its windows now
  wait for Erenshor's native EventSystem, preventing duplicate input dispatch
  and the recurring `There can be only one active Event System` warning during
  normal play.
- All ordinary addon items now pass Erenshor's native sell, trash, and bank
  eligibility gates. This includes recipe scrolls, Monk tomes, Necromancer
  scrolls, raid-scroll duplicates, and the Necromancer charm mold.
- Empty crafted bags can be sold or destroyed; filled crafted bags remain
  protected from those loss-causing operations but can be banked with their
  exact contents. Corpse Bags remain recovery-only by design.
- The full in-game smoke catalog now rejects any future ordinary custom item
  with a zero vendor value or a sell/trash restriction flag.

## Playable Monk class

Added Monk as a complete selectable class rather than a cosmetic skill set.

- Appears in character creation with its own native-style class icon and uses
  the same starting-stat budget envelope as Erenshor's native classes.
- Adds **29 uniquely named Monk techniques** across levels 1-35. Monk also
  receives the seven compatible native martial abilities, bringing its total
  progression to **36 abilities** without Roman-numeral repeats.
- Techniques cover sustained hand-to-hand attacks, kicks, counters, stances,
  healing, cleansing, feign death, defense, haste, mobility, and capstones.
- Level requirements are enforced when an ability is used. Monk tomes cannot
  be learned by another class.
- Uses normal native endurance, cooldown, skill-use, aggro, damage, status,
  combat-log, and save paths.
- Live comparative balance testing completed **576/576 parses** at levels 5,
  10, 15, 20, 25, 30, and 35 across budget, progression, and optimized
  class-appropriate gear. Raid-drop spells such as Summon Treant were excluded.
- A focused post-tuning run completed **36/36 parses**, and all **24/24** final
  guard cases passed. The level-20 technique curve is stronger, level-30 to
  level-35 growth is smoother, and Way of Iron now adds 15% technique damage
  per rank. No native class value was changed for Monk balance.

## Real punch, kick, and Flying Kick motion

Monk attacks no longer leave the character standing still while colorful VFX
hit the enemy.

- Retargets Erenshor's authored humanoid **PunchSwingRight** motion at runtime
  for punches. The clip is loaded from the installed game and is not copied or
  redistributed in this addon.
- Uses native player Kick and Jump states for the kick family, then layers
  technique-specific hips and torso motion for readable weight transfer.
- Round Kick and Crescent Heel turn through their strikes; Sweeping Crescent
  drops into a low rotation; Whirlwind Kick completes a full-body spin; Dragon
  Punch rises through impact; Fist of Falling Rain performs a three-hit punch
  sequence.
- Pummel delivers a six-hit alternating-fist barrage with visible flying
  knuckle afterimages and contact-timed impacts.
- **Flying Kick now visibly launches the character upward and forward into the
  target**, connects in the air, and settles during recovery.
- The save-compatible Stone Throw slot is now **Resonant Palm**, an original
  Hadouken-style two-palm technique with a licensed authored hip charge,
  planted forward thrust, visible traveling teal energy sphere, wake, and
  target-side compression bloom. Its level, damage, range, pull, cooldown,
  IDs, and saved progression are unchanged.
- Whirlwind Kick now holds a real roundhouse contact silhouette through one
  eased 360-degree body turn, keeps its support foot planted, and carries
  layered heel-orbit, cyclone, and ground-wake VFX through the rotation.
- Main-hand and off-hand handwrap autoattacks both play visible punch motions.
- Handwraps now suppress Erenshor's generic one-handed `MeleeSwing`,
  `DualWield`, `DoubleAttack`, and `OHDoubleAttack` animation paths, so an
  unarmed Monk no longer appears to swing an invisible sword.
- Autoattacks cannot interrupt the visual motion of an active Monk technique.
- Each hit and its VFX wait for that move's authored impact time. Light attacks
  snap quickly; committed finishers retain a readable windup and recovery.
- Temporary animator overrides are restored and destroyed after use so zoning,
  class changes, and normal native animations do not retain stale state.

## Monk VFX and artwork

- Added a bespoke VFX treatment for every one of the 28 active Monk techniques.
  Offensive contact effects follow the correct strike frame instead of firing
  when the button is pressed.
- Added 29 unique technique icons and 29 matching unique tome icons.
- Added five unique aura-effect icons, five aura-item icons, seven class-gear
  icons, and one Monk class icon: **76 new runtime sprites** in total.
- Icons use the same compact painted-fantasy readability and framing expected
  by Erenshor's spell, inventory, and character UI.

## Monk auras and Ascensions

Added five class-only auras using the native Aura slot and save fields. Each
mirrors the complete stat budget of the corresponding native Duelist aura:

- Stillwater Breath
- Flowing Reed
- Iron Body
- Path of the Gale
- Grandmaster's Presence

The starter Aura Gem merchant receives the level-appropriate Monk aura. Added
five persistent three-rank Ascension perks to the Monk class tab:

- **Way of Iron** — increases Monk technique damage.
- **Deep Mend** — improves Mend's maximum-health restoration.
- **Flowing Defense** — improves Stone Stance mitigation.
- **Lingering Flurry** — extends Hundred Fists.
- **Perfect Stillness** — extends Feign Death.

## Monk trainers and class gear

Added one contextually placed Monk trainer to each required hub. Each trainer
inherits native merchant presentation and stands beside a relevant existing
merchant rather than appearing at an arbitrary coordinate:

- **Master Ren of the Open Hand** — Stowaway's Step
- **Sera Windstep** — Willowwatch
- **Grandmaster Kalen** — Port Azure

Their inventories are level-sorted and contain techniques, auras, and Monk
equipment. Seven Monk-only native-budget sidegrades were added:

- Initiate's Headwrap
- Windstep Gi
- Silent-Step Sandals
- Grandmaster's Legwraps
- Novice Handwraps
- Fists of the Tranquil Storm
- Grandmaster's Prayer Beads

Armor uses light native visuals. Handwraps are Primary-or-Secondary weapons
and intentionally keep both hands unobstructed so the new punch animations are
visible. Non-Monks cannot equip the set.

## Necromancer shared combat abilities

- Necromancer now receives the same compatible native shared combat abilities
  available to Arcanist.
- Mouse, gamepad, tooltip, learn, and persisted ability-list paths recognize
  those shared entries without renaming or cloning the native abilities.
- This does not change the Necromancer's 35 custom-spell progression or alter
  any base class.

## Controlled in-game verification

All checks below ran in the actual Steam game on the dedicated **John
EverQuest** QA character.

- Release build: **0 warnings, 0 errors**.
- Current full in-game smoke: **1,401/1,401 passed** across all **833 addon
  items**, **280 recipes**, and **197 equippable visuals**. The catalog now
  verifies native sell/trash eligibility for every ordinary addon item, exact
  inventory/bag round-trips, one native EventSystem, and exact gear-state
  restoration.
- Necromancer real-cast sweep: **35/35 passed** through Erenshor's live charge
  and resolve pipeline with custom-only caster VFX, including the PBAOE status
  guarantee for Infectious Cloud.
- Active Bone Thrall movement check passed with exact effective-speed parity:
  **9.25 pet / 9.25 owner**.
- Body-motion storyboard: **48/48 screenshots captured** — windup, contact,
  and recovery for all 14 offensive techniques plus main-hand and off-hand
  handwrap autoattacks.
- Motion manifest: **16/16 sequences played**, including the visible airborne
  arc for Flying Kick.
- Final exact-timestamp storyboards captured **18/18 frames** for Resonant Palm
  and Whirlwind Kick. Whirlwind retained its contact silhouette through the
  complete planted spin; Resonant Palm retained its crouched charge,
  two-palm release, and grounded recovery.
- Contact VFX sweep: **14/14 passed** with each effect captured on the posed
  character at impact.
- Final targeted VFX checks passed **6/6** frames: charge/travel/impact for
  Resonant Palm and windup/direction/impact for Whirlwind Kick.
- Native mechanics sweep: **29/29 passed** through Erenshor's real `DoSkill`
  pipeline against an isolated native test target.
- Pummel separately passed native damage resolution and produced a complete
  nine-frame motion storyboard plus windup, direction, and impact VFX captures.
- Final targeted mechanics passed **2/2** through native damage resolution:
  Resonant Palm dealt 935 and Whirlwind Kick dealt 1,831 on the controlled
  level-35 target.
- Exact native main-hand and off-hand triple-attack probes passed **2/2**
  `PlayerCombat.PerformAttacks` calls and **6/6** strike captures with no
  generic weapon-trigger leaks, pose failures, or recovery failures.
- The full Monk DPS audit completed **576/576** live parses, the focused retune
  completed **36/36**, and every final balance guard passed.
- Runtime humanoid validation found hips, torso, hands, and feet and confirmed
  that the authored punch clip was retargeted to the player.
- No exception, null reference, missing method, bad argument, or index error
  appeared in the final runtime log.
- Lunaris 0.1.9 previously verified the v1.8 loader path by real plugin ID with
  **125 patches and 0 skipped**. The current Lunaris archive contains the same
  final DLL hash as the BepInEx archive and standalone DLL; its layout and
  checksums were revalidated for Erenshor build 24405256.
- A final shipping-mode BepInEx launch, with test mode disabled, also applied
  **124 production patches with 0 skipped** and produced no addon error or
  exception. Test mode adds ten QA-only patches and remains disabled in the
  installed shipping configuration.

## New test-mode commands

- `/cstest monk motion <name>` captures a VFX-free nine-frame storyboard for
  one strike.
- `/cstest monk motions` captures windup, contact, and recovery for every
  offensive technique and both handwrap autoattacks.
- `/cstest monk capture strikes` captures all offensive moves with their real
  contact-timed VFX.
- `/cstest monk mechanics` exercises all 28 techniques through native skill
  execution against an isolated test target.
- `/cstest monk autoattack [side]` exercises the exact native main-hand and
  off-hand multi-attack paths and captures every handwrap strike.
- `/cstest monk dps <full|monk-retune>` runs the comparative multi-level,
  multi-gear balance audit or its focused verification matrix.
- Existing Monk status, levels, icons, auras, new-gear, Ascension, and trainer
  checks remain available. Mutation commands are identity-guarded to John
  EverQuest.

## Installation / upgrade

### BepInEx 5

Extract the BepInEx ZIP into the Erenshor directory and replace the older DLL:

`Erenshor/BepInEx/plugins/ErenshorClassicSkills.dll`

### Lunaris

1. Install and launch Lunaris once.
2. Remove an older Classic Skills DLL from `Erenshor/plugins/`.
3. Extract the Lunaris ZIP into the game directory, producing
   `Erenshor/plugins/ErenshorClassicSkills.dll`.
4. Do not leave a second copy in `BepInEx/plugins/` and do not run both loader
   bootstraps together.
5. Enable **Erenshor Classic Skills** in Lunaris's plugin installer or with
   `/lunaris plugins`, then restart Erenshor once before entering a character.

## Release safety

Controlled test mode and debug chat commands are disabled in the shipped
configuration. Future QA must explicitly enable them.
