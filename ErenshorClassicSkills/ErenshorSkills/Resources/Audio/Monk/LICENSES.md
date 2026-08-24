# Monk audiovisual source assets

The following source packs are dedicated to the public domain under Creative
Commons CC0 1.0. The addon embeds edited derivatives rather than the original
archives.

## Kenney Impact Sounds

- Source: https://www.kenney.nl/assets/impact-sounds
- License: Creative Commons CC0 1.0
- Derivatives: `impact_medium_a.wav`, `impact_medium_b.wav`,
  `impact_heavy_a.wav`, `impact_heavy_b.wav`, and `focus_bell.wav`
- Edits: converted to mono PCM WAV, trimmed, faded, and level-adjusted.

## BigSoundBank Whoosh #1

- Source: https://bigsoundbank.com/whoosh-1-s0572.html
- Author: Joseph SARDIN
- License: Creative Commons CC0 1.0 / public domain
- Derivatives: `whoosh_fast.wav`, `whoosh_broad.wav`, and
  `whoosh_heavy.wav`
- Edits: isolated individual movements, converted to mono PCM WAV, trimmed,
  faded, and level-adjusted.

## Kenney Particle Pack

- Source: https://www.kenney.nl/assets/particle-pack
- License: Creative Commons CC0 1.0
- Derivatives in `Resources/Vfx/Monk`: `impact_soft.png`,
  `impact_flare.png`, `impact_slash.png`, and `motion_twirl.png`
- Edits: selected from the transparent PNG set and renamed for their runtime
  roles. Runtime tint, scale, rotation, and fade are authored by the addon.

## Joseph SARDIN — BigSoundBank (martial impacts)

- Source: https://bigsoundbank.com
- Author: Joseph SARDIN
- License: Creative Commons CC0 1.0 / WTFPL / public domain. Redistribution,
  adaptation and commercial use are explicitly permitted.
- Derivatives: `punch_light.wav` (Punch #13, `s2468`), `punch_mid.wav`
  (Punch #6, `s2461`), `punch_heavy.wav` (Punch #8, `s2463`),
  `punch_hard.wav` (Punch #10, `s2465`), `punch_crack.wav` (Punch #12,
  `s2467`), `punch_body.wav` (Punch #4, `s2459`), `whip_fast.wav`
  (Whip crack #1, `s2949`), `whip_broad.wav` (Whip crack #3, `s2951`)
- Edits: decoded from MP3, downmixed to mono, resampled to 44.1 kHz 16-bit PCM.
  Each punch source carries 100-160 ms of silence before the strike, which is
  trimmed off so the impact is not delayed. Faded and loudness-normalised to
  -14 LUFS with a -1 dBTP ceiling.

## rubberduck — 80 CC0 RPG SFX (textures)

- Source: https://opengameart.org/content/80-cc0-rpg-sfx
- Author: rubberduck (OpenGameArt)
- License: Creative Commons CC0 1.0
- Derivatives: `knuckle_dry.wav` (`wood_03.ogg`), `stone_heavy.wav`
  (`stones_01.ogg`), `metal_ring.wav` (`metal_03.ogg`), `aura_shimmer.wav`
  (`item_gem_04.ogg`), `aura_tick.wav` (`item_gem_02.ogg`), `body_grunt.wav`
  (`creature_hurt_02.ogg`)
- Edits: as above.

## Superseded

`impact_medium_a/b.wav` and `impact_heavy_a/b.wav` (Kenney Impact Sounds) are
retained but no longer referenced; the BigSoundBank punch series replaced them
when runtime synthesis was removed and the recordings became the whole signal.
