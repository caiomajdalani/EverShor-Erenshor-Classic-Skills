# Monk ability icon sources

The Monk ability icons are reproducibly assembled by
`tools/generate_monk_ability_icons.py`.

- `monk-ability-medallion-chroma.png` is the original generated frame on a
  removable green background.
- `monk-ability-medallion.png` is the locally keyed transparent frame used by
  the generator.
- `monk-tome-base.png` is the addon's existing Monk tome frame, retained as the
  common base for readable item icons.
- `game-icons/*.svg` are selected hand-drawn glyphs from Game-Icons.net. Their
  authors and CC BY 3.0 license are recorded in `THIRD_PARTY_NOTICES.md`.

Final icons use a single subdued iron/bronze frame, muted class colors, and
high-contrast symbols so they remain readable at Erenshor's small UI sizes.

The medallion prompt requested a weathered late-1990s/early-2000s fantasy RPG
UI frame with a near-black empty center, aged bronze and charcoal iron, tiny
desaturated teal accents, no symbol, no text, and no glossy/mobile-game finish.
