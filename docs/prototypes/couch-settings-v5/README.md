# Couch Settings — round 5: the other sections in the Emulators language (2026-09-05)

PR #228 shipped the Emulators section as one compact summary row per platform (56 dip), compact
one-line child rows (66 dip), state-first descriptions, problems stated in place in the warning colour,
Y as the row's secondary action. Every other section still has the old 84+ dip rows with paragraph
descriptions. This round proposes each of them in the shipped Emulators language.

Open `index.html`. Space / click cycles **Current** (real Thor screenshot from 1 Sep, or the headless
1280×800 render for the Desktop-only Texture Packs) → **Round 5** (first pass, kept as images under
`round5/`) → **Polished**; ←/→ switch screens.
`index.html?s=N&bare=1` renders one proposed screen bare at 1920×1080 (headless Chrome).

Screens: Library · RetroAchievements (signed out / signed in) · Artwork & Metadata · Saves (list /
PlayStation opened) · Themes · About · Texture Packs (Desktop couch only). Correction after
implementation: the theme catalogue has thirty themes, not six, so the Themes gallery still scrolls;
the compact toggles buy a full first row of cards in view. Notes under each screen say
what changes and why. Emulators is not drawn — it is the reference.

**Polished pass (same day):** one control vocabulary. A row ends in exactly one of four things —
a chevron `›` (A does what the label says; red on destructive rows), a switch with no caption (the
description's first word states the value), `‹ value ›` for a choice, or a plain value for read-only
rows. Gone: the seven glyph circles (↻ › ↓ ↑ ✎ × +) in three colours, the SHOW/HIDE · AUTO/MANUAL ·
CLOSE/KEEP · ON/OFF switch captions, muted circles on disabled rows (the whole row dims instead), and
per-row A captions in the legend (A is always Select, as shipped; only the Y caption changes).

**Third pass (same evening):** account rows follow one rule — *Y on an account row disconnects it*.
ScreenScraper is one row with the account name as its value (no summary row, no expansion when
signed in; signed out it opens Username / Password / Connect beneath it); RetroAchievements' Account
row works the same, so its separate Disconnect row is gone; Google Drive already did this. Every row is
the same card (the flat transparent "info" rows are gone; read-only rows end in a plain value).
Platform icons are the app's own art (`Assets/PlatformConsoleArt` for PS2 / PS3 / PSP / Wii / 3DS), so
PlayStation, PlayStation 2 and PlayStation 3 are three different consoles. The legend starts with
D-pad · Navigate as shipped, and the Desktop-only Texture Packs screen shows the Desktop couch rail.

The same three moves everywhere:
1. **Compact rows** (the shipped `compact` style) and one-line, state-first descriptions
   ("Off · …", "Connected · last sync …", "Last scan today · nothing changed").
2. **Header rows go.** Where a section had a per-thing header plus 4–5 rows (Saves platforms,
   ScreenScraper account, texture-pack platforms) the thing becomes a **summary row that expands in
   place**, one open at a time, exactly as Emulators does. Where the header only repeated the section
   title (RetroAchievements sign-in, "Web image search") it is simply dropped.
3. **Problems in place + in the rail**: skipped saves, missing covers, packs needing attention appear
   on the row that fixes them and as the section's rail status, like "PlayStation 2 needs attention".

Data on the proposed screens is the Thor's on 1 Sep (981 games / 14 platforms, PS2 → PCSX2 missing,
Drive synced 21 Aug 18:29 with 6 PlayStation saves skipped, ScreenScraper connected, theme Abyss,
1.7.8 @ 66e2c5273). Lines that need data the section does not compute today are flagged in the notes:
games without a cover, per-platform last-sync text, RetroAchievements match count.

Assets: `assets/shelf-bg.jpg` + `assets/icons/*` from the earlier couch-settings prototypes; the font is
the repo's bundled Exo 2 (`src/EmuShelf.UI/Assets/Fonts/Exo2.ttf`).
