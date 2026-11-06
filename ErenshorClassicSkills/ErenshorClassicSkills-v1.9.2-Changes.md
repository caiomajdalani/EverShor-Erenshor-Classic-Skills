# EverShor 1.9.2 — Monk actor isolation follow-up

Date: 2026-11-05. Included in existing PR #2 together with the earlier 1.9.1 corrections. The PR branch retains its original release/1.9.1 name so the existing review remains intact; plugin, project and manifest identify version 1.9.2.

- Fix Resonant Palm enabling local-player auto-attack when the real caster is a Sim. Only the actual player caster may auto-engage, respecting the native option.
- Give Resonant Palm and Pummel follow-up messages the actual caster's name and the correct player/other combat-log category.
- Check the follow-up caster's class and combat state rather than the local player's class. Cancel queued punches on retarget, death, moving out of melee reach or disabling Monk.
- Retain all previous 1.9.1 class/target isolation corrections and the restored complete gameplay README.
- Preserve damage tuning, cooldowns and learned-ability save data.

Validation: 33 production-policy regressions pass; Release compilation, reference-target, icon and documentation checks pass. Two existing MSB3277 warnings remain. The latest auto-attack fix still requires confirmation in the game; other Monk positioning/animation issues remain under investigation.
