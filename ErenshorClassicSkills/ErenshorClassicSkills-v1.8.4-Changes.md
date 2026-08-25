# Erenshor Classic Skills 1.8.4

## Fixes

- Restore ordinary inventory, bank, and vendor clicks under both BepInEx and
  Lunaris.
- Prevent login from stopping after auction-house filling when SimPlayer spell
  or held-item data is only partially initialized.
- Cap beginner-zone Stowaway merchant Ossira at level 10.
- Let Willowwatch merchant Morthane teach through level 26.
- Put the advanced level-27–35 catalog on Veyra in Port Azure.
- Keep every merchant's stock level-sorted.
- Persistently assign about one eighth of eligible SimPlayers to Necromancer
  and one eighth to Monk without changing Erenshor's native save schema.
- Preserve safe Arcanist and Duelist native fallbacks while restoring the
  custom runtime class, level-appropriate combat kit, and CC/DPS roles.
- Give Necromancer and Monk Sims class-specific greetings, LFG, aggro, death,
  level-up, and outing lines.
- Add general SimPlayer chatter about both new classes.
- Store custom identities in a loader-neutral, validated sidecar with atomic
  writes and backup recovery.
