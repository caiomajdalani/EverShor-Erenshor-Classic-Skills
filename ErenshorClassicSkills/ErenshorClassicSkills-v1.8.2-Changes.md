# Erenshor Classic Skills 1.8.2

- Make Erenshor's native character-selection, backup-selection, enter-world,
  and save paths preserve `Necromancer` and `Monk` as first-class identities.
- Recover a previously overwritten custom-class save when its unique
  Necromancer spell IDs or Monk skill IDs still prove the original class.
- Enforce the saved custom class during play so a native or third-party class
  reassignment is immediately reversed before class runtime logic executes.
- Teach mapped Necromancer and Monk skill books directly at their configured
  levels without temporarily changing the player's class to Arcanist,
  Duelist, or Paladin.
- Add deterministic load/save identity checks for both custom classes and keep
  the real mouse/controller Necromancer book-use regression coverage.
