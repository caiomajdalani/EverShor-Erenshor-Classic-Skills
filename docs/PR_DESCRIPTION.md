# Proposed 1.9.0: native Lunaris migration, class UI and Thrall equipment/combat

Base: `krautchanpro/EverShor-Erenshor-Classic-Skills:main`, tag 1.8.4 / ff821f1d2cfce194f792c68749e6dd51491eb5ea.

This contribution ports Classic Skills to native Lunaris and consolidates locally developed character-selection and Necromancer/Thrall improvements. Original plugin name, GUID, author attribution and license/asset notices are preserved.

Users migrate from a BepInEx entry point to Lunaris Options and commands. Monk/Necromancer appear on their own class-selection page instead of expanding the native class grid. Thralls receive slot-validated equipment, gear-derived combat values and a private tier-based ability kit; they no longer inherit the player's learned spellbook. Optional Fate of the Fallen page support stays behind reflection.

Cleanup removes unused layout code, repeated ability-membership scans and routine taunt-query allocations, releases spell/UI caches on reset/unload and replaces the chat-color fallback chain with explicit settings delegates. The periodic-effect guard now propagates unrelated exceptions instead of hiding errors globally. Previously tracked bin/obj outputs and third-party lib references are removed from the source tree and ignored for local development.

This is a gameplay change as well as a migration: direct taunt is 1.25 s, area taunt is 2 s/12 m, attacks are 6 s, slow 11 s and stun 15 s. Please review the balance independently of the loader fixes. Changes are inventoried in `docs/CHANGE_INVENTORY.md` and the root changelog.

Validation: Release and Debug builds pass with zero errors and two documented MSB3277 reference-version warnings. Twelve production-policy regressions pass; metadata and 644 icon checks pass; 86 literal Harmony target names resolve. The Bash script was not run because no Bash executable is available locally. CI runs reference-free checks because game/runtime DLLs are local dependencies.

**Game runtime tests have not been performed in this audit.** `docs/VALIDATION.md` lists the required save-migration, UI, control/immunity, balance, inventory persistence, cross-mod and shutdown scenarios. The managed exception guard is not a claim to fix every Unity crash. Loader options are not auto-imported; users should back up saves and review Options. The existing upstream root/nested license distinction is preserved for maintainer clarification.

Not included: Item Compare, Diagnostics, game/runtime binaries, private saves, SDK caches or the cancelled Death Knight concept. Nothing has been published to the upstream repository by this preparation.
