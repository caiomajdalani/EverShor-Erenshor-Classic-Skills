# Erenshor Classic Skills 1.8.3

- Restore ordinary left/right-click inventory, bank, and vendor interaction by
  routing Necromancer and Monk book use through one shared native `ItemIcon`
  patch instead of four loader-sensitive wrappers.
- Prevent BepInEx and Lunaris login/zone loading from stopping at the auction
  house/finalizing boundary when a SimPlayer encounters a null spell class
  list, partially initialized spell collection, null equipment entry, or an
  unresolved held item.
- Preserve unresolved SimPlayer held-item save entries while safely excluding
  them from the current native spell-load pass.
- Extend live regression coverage to prove ordinary left-click cursor pickup,
  custom-class mouse/controller book use, null `Spell.UsedBy` repair, and
  unresolved held-item preservation.

## Necromancer spell acquisition

The three Necromancer trainers in Stowaway's Step, Willowwatch, and Port Azure
all sell the same level-sorted catalog. The vendor progression ends at level 26
because the remaining eight scrolls are intentionally obtained as drops:

- Soul Transfusion (25): Priel Consort, Priel Malarist, or Priel Purist.
- Devouring Darkness (27): Darkwarden Awxa, Ealdred, or Ilyth.
- Drain Spirit (29): Molorai Nightmistress.
- Gravefire Immolation (30): Azynthian Firebone or Greater Azynthian Firebone.
- Ossuary Champion (32): Boneweaver or Crypt Keeper.
- Dread Shield (33): Expert Gladiator or Wandering Gladiator.
- Harmshield (34): Solunarian Paladin, Moonkeeper, or Sunbringer.
- Plague of Souls (35): Faerie Trickster, Pack Mother, or the listed Planar
  Cobra, Crawler, Faerie, Grizzly, Minotaur, and Wolf encounters.

## Verification

- Release build: zero warnings and zero errors.
- Regression tests: ordinary inventory click, custom book use, and SimPlayer
  spell-load hazard cases added to the live QA suite.
