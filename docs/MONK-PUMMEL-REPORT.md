# Pummel follow-up attribution — 1.9.2 local preview

Reported setup: 1.9.1, player has not learned Pummel, a Sim Monk is nearby. The screenshot displays repeated "Pummel strikes ... for 1/2 physical damage" alongside the player's Tiger Claw action.

## Source findings

The exact Pummel message is emitted by MonkRuntime.FollowUpStrikes. Pummel unlocks at level 5 and declares five extra strikes after its opening hit. These supplemental hits have a separate low damage roll; they are not five repetitions of the initial Tiger Claw hit.

The previous routine already passed the real caster into Character.DamageMe, but logged every follow-up in PlayerHits with no caster name. It also checked IsMonkPlayer, making a Sim's follow-up depend on the local player's class. Thus the screenshot can show a Sim's Pummel as though it belonged to the player. The screenshot alone does not establish an unlearned player ability or identify which Sim produced every hit.

## Preview changes

- Local follow-ups say "You use Pummel ... (follow-up)" in PlayerHits.
- Sim follow-ups include the actual NPC name and use OthersHits.
- Eligibility depends on the caster's class/life and current combat target, not the local player's class.
- Queued punches stop after a target switch, death, moving outside melee reach or disabling Monk.
- Damage values, ability learning and Pummel's cooldown are not retuned. No learned-ability save data is deleted.

Six added production-policy regressions cover caster class, independent Sim eligibility and queue cancellation; 33 policy regressions pass in total after adding four preview-2 Resonant Palm auto-engage checks. Source/reference/icon/link checks and Release compilation pass with the existing two MSB3277 warnings. Runtime reproduction with the reporting player remains pending.

## Tester check

Use a Monk who has not learned Pummel alongside a Sim Monk who can use it. Confirm that small follow-up lines name the Sim. Also repeat with a non-Monk player: the Sim's ability should no longer depend on the player's class. If an actual "You use Pummel" line appears for a player without it, retain the build version and combat log so the activation path can be investigated separately. This correction is now included in PR #2 as part of version 1.9.2.
