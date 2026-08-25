# Erenshor Classic Skills 1.8.4

## Necromancer spell progression

- Cap beginner-zone Stowaway merchant Ossira at level 10.
- Let Willowwatch merchant Morthane teach through level 26.
- Put the advanced level-27–35 catalog on Veyra in Port Azure.
- Keep every merchant's stock level-sorted and validate the native vendor
  window in all three zones.

## Necromancer and Monk SimPlayers

- Persistently assign about one eighth of eligible SimPlayers to Necromancer
  and one eighth to Monk without changing Erenshor's native save schema.
- Preserve safe Arcanist and Duelist native fallbacks while restoring the
  custom runtime class, level-appropriate combat kit, and CC/DPS roles.
- Give Necromancer and Monk Sims class-specific greetings, LFG, aggro, death,
  level-up, and outing lines.
- Add general SimPlayer chatter about both new classes.
- Store custom identities in a loader-neutral, validated sidecar with atomic
  writes and backup recovery.

## Verification

- Release build completed with zero warnings and zero errors.
- BepInEx and Lunaris both completed login and entered Stowaway.
- Live QA verified 30 Necromancer and 30 Monk SimPlayers, persistence, native
  fallbacks, combat kits, roles, and chatter.
- Stowaway, Willowwatch, and Port Azure vendor inventories passed at level caps
  10, 26, and 35 respectively.
