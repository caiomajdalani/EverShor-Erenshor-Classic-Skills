# Unexpected auto-attack during guild UI — 1.9.2 preview 2

A tester reported auto-attack enabling while using the guild menu after receiving the Pummel follow-up build; the screenshot itself does not verify the loaded assembly. This is distinct from anonymous Pummel log attribution.

## Concrete source defect

MonkRuntime.ActivateStoneThrow implements Resonant Palm, available from Monk level 7. After accepting a cast, it unconditionally called GameData.PlayerCombat.ForceAttackOn, even when the actual caster was a Sim. A Sim using Resonant Palm could therefore enable local-player auto-attack while the player was doing something unrelated. The helper also ignored the game's AutoEngageAttackOnSkill preference.

The same special damage resolver printed "You use Resonant Palm" and used PlayerHits for Sim casts. This can explain the earlier apparent cross-class skill-use messages without establishing that the player's learned spellbook actually contained Resonant Palm.

## Preview 2 correction

- Only a cast by the actual local player may enable that player's auto-attack.
- Respect AutoEngageAttackOnSkill, matching the native skill preference.
- Attribute both Resonant Palm and supplemental Pummel damage to the real caster in the appropriate player/other log category.
- Do not show player-facing range/incapacitation messages for failed Sim Resonant Palm attempts.
- Keep all Pummel follow-up fixes from preview 1.

The actor/preference truth table has four linked production-policy tests. Together with the follow-up tests and earlier combat checks, 33 regression cases pass. Release compilation and source/reference/icon/documentation checks pass with two existing MSB3277 warnings. Native UI/game reproduction is still required: this closes a specific erroneous global call, not every possible source of attack activation.

## Reproduction and confirmation

With a Sim Monk of level 7+ fighting nearby, leave the player out of attack mode and interact with the guild UI. A Sim's Resonant Palm must name the Sim and must not enable player auto-attack. Repeat while the player is a non-Monk. For a player Monk, casting Resonant Palm should auto-engage only when the native option is enabled. This correction is now included in PR #2 as part of version 1.9.2.
