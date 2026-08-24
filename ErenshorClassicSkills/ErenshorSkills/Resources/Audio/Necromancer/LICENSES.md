# Necromancer audio source assets

Every file in this directory derives from a source dedicated to the public
domain under Creative Commons CC0 1.0. The addon embeds edited derivatives
rather than the original archives. No attribution is legally required by CC0;
the credits below are given voluntarily.

## rubberduck — 80 CC0 RPG SFX

- Source: https://opengameart.org/content/80-cc0-rpg-sfx
- Author: rubberduck (OpenGameArt), published 8 July 2018
- License: Creative Commons CC0 1.0
- Derivatives:

  | File | Source clip |
  | --- | --- |
  | `soul_draw.wav` | `spell_02.ogg` |
  | `transfer_pulse.wav` | `spell_01.ogg` |
  | `fire_pyre.wav` | `spell_fire_06.ogg` |
  | `disease_wet.wav` | `creature_slime_02.ogg` |
  | `disease_bolt.wav` | `creature_slime_04.ogg` |
  | `shadow_low.wav` | `creature_roar_01.ogg` |
  | `death_wail.wav` | `creature_roar_02.ogg` |
  | `fear_roar.wav` | `creature_roar_03.ogg` |
  | `death_knell.wav` | `creature_die_01.ogg` |
  | `fear_shriek.wav` | `creature_hurt_01.ogg` |
  | `covenant_chain.wav` | `chain_01.ogg` |
  | `ward_tick.wav` | `item_gem_01.ogg` |
  | `ward_shimmer.wav` | `item_gem_04.ogg` |
  | `ward_heavy.wav` | `metal_01.ogg` |

- Edits: decoded from Ogg Vorbis, downmixed to mono, resampled to 44.1 kHz
  16-bit PCM, trimmed to the useful transient, short in/out fades applied, and
  loudness-normalised to -14 LUFS with a -1 dBTP ceiling.
- `ward_heavy.wav` additionally drops the first 195 ms of `metal_01.ogg`, which
  is silence before the strike. Left in place it delayed every ward by about a
  fifth of a second.

## Joseph SARDIN — BigSoundBank

- Source: https://bigsoundbank.com
- Author: Joseph SARDIN
- License: Creative Commons CC0 1.0 / WTFPL / public domain. Redistribution,
  adaptation and commercial use are explicitly permitted; attribution is
  requested but not mandatory.
- Derivatives:

  | File | Source clip |
  | --- | --- |
  | `bone_crack_a.wav` | Bones cracking #2 (`s1408`) |
  | `bone_heavy.wav` | Bones cracking #2 (`s1408`), untrimmed head |
  | `bone_crack_b.wav` | Bones cracking #3 (`s1409`) |
  | `blood_heart.wav` | Heartbeat #1 (`s0243`) |
  | `disease_gas.wav` | Air leak (`s1486`), 1.0 s excerpt |

- Edits: decoded from MP3, downmixed to mono, resampled to 44.1 kHz 16-bit PCM,
  excerpted, faded, and loudness-normalised as above.
- `disease_gas.wav` additionally has a raised-sine swell envelope with a 7 Hz
  turbulence ripple applied. The raw excerpt is steady-state hiss with no onset
  or decay, which reads as tape noise rather than as escaping gas; the envelope
  gives it the event shape a spell layer needs.

## Not used

Kenney's RPG Audio pack (https://kenney.nl/assets/rpg-audio, CC0 1.0) was
evaluated for this set and contributed no files. Despite its name it contains
foley only — books, doors, footsteps, cloth and cookware — with no spell or
creature material. It is recorded here so the evaluation is not repeated.
