# Erenshor Classic Skills v1.4.2

Merchant inventory quality-of-life update for Erenshor Steam build
**24247002** (July 17, 2026).

## Recipe scroll ordering

- Recipe scrolls now appear after a merchant's normal stock, unlocked quest
  rewards, crafting materials, and other non-recipe merchandise.
- The ordering is stable: normal merchandise keeps its existing order, and
  recipe scrolls keep their existing order within the final recipe pages.
- Vendor lists retained in the former recipes-first order are repaired whenever
  the merchant window opens.
- Completed-quest rewards are accounted for before sorting, matching Erenshor's
  native unlock conditions and preventing native vendor processing from placing
  rewards behind the recipe pages.
- The vendor integration runs at final Harmony prefix priority so merchandise
  added by the game and most other addons is included in the same ordering pass.

## Verification

- Added a deterministic smoke check that begins with interleaved stock and
  recipe entries, then verifies exact stable stock-first/recipes-last ordering.
- Release build completed with **0 warnings and 0 errors**.
- The full in-game smoke run completed with **1,256 passed and 0 failed** on
  Erenshor Steam build 24247002.
- Live merchant paging was visually inspected at Treven Pines: pages 1-5 held
  normal stock and crafting materials, page 6 began recipe scrolls, and the
  final page contained recipe scrolls with no later non-recipe merchandise.
- Release DLL SHA-256:
  `5ce575766e001e882145f2c2c32b0d33f114feaec959e8f85f5cedfd6c939c77`.

## Installation

- **BepInEx:** extract the BepInEx archive into the Erenshor game directory. The
  DLL installs to `BepInEx/plugins/ErenshorClassicSkills.dll`.
- **Lunaris:** extract the Lunaris archive into the Erenshor game directory. The
  DLL installs to `plugins/ErenshorClassicSkills.dll`, then enable it through
  Lunaris's plugin interface or `/lunaris plugins`.

Do not install both loader packages at the same time. Choose the package matching
the loader in use.
