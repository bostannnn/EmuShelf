# Couch Settings — round 6: ScreenScraper and Themes, regrouped

Two screens Andrew called out after the round-5 build landed on the Thor (2026-09-06):

- **ScreenScraper** — *"that's just 3 rows not connected in any meaningful way."*
- **Themes** — *"them and top screen settings (crt toggle for example) are fighting for attention."*

Three directions each, drawn at the shipped geometry (rows 66 dip, summaries 56, cards 3 across the
1228-wide content column). The Thor's Settings panel is **833×468 dip** — 1920×1080 at density 369 —
so what fits in these mocks fits on the device.

Open `index.html`; click the stage or press <kbd>Space</kbd> to cycle **Today → A → B → C**, and
<kbd>←</kbd>/<kbd>→</kbd> to switch screens. `?s=N&v=K&bare=1` renders one variant bare at 1920×1080
for headless capture.

**Not screenshots.** "Today" is redrawn from the shipped code, not captured from the device, because
the Thor was in Andrew's hands. Everything else is real: the thirty palettes come from
`ThemeCatalog.cs` (parsed into `themes.js`), the selected theme is his actual `Abyss`, and both
toggles are drawn off as his `settings.json` has them.

## ScreenScraper

| | |
|---|---|
| **Today** | `ScreenScraper username`, `ScreenScraper password`, `Connect ScreenScraper` — three full-weight rows under three unrelated ones. Nothing binds them, the word is said three times, and the *connected* state is already a single row, so the section changes shape entirely depending on sign-in. |
| **A — one row, one task** | Signing in is a task, not three settings: Artwork settles at four rows and A on the ScreenScraper row opens a sign-in panel holding both fields and the button. Also the only direction that says *why* you would sign in. Costs a new overlay (the text-entry overlay models it). |
| **B — expand in place** | ScreenScraper becomes a summary row that opens beneath itself, exactly like a platform in Emulators or Saves. Zero new vocabulary — the indent is what binds the rows. Still three rows once open. |
| **C — regroup the section** | The real muddle is that the section interleaves *where artwork comes from* with *when it is fetched*. Ordered as three sources then two timing rows, ScreenScraper becomes one source among peers, and the built-in catalogue stops being invisible. Biggest rewrite; grouping without headers leans on order alone. |

## Themes

| | |
|---|---|
| **Today** | The two switches sit at the top in full-weight rows, above the gallery — two jobs at one weight, with the switches holding the position the eye lands on, and one row of cards in view instead of two. |
| **A — Themes is only themes** | Both switches leave for their own **Display** rail entry (drawn dashed); the gallery starts at the top and gains a full row of cards. Costs an eighth rail entry, and the rail already scrolls on the Thor. |
| **B — gallery leads, switches become a footer** | Nothing moves sections; the hierarchy is just made honest. Cards take the page, the switches drop to a slim two-up strip below a divider in secondary type. Smallest change. Costs a control position that exists nowhere else in Settings. |
| **C — the preview owns the effects** | CRT and artwork-matching are *effects layered on the theme*, so they move into a preview panel showing the selected theme as a miniature shelf, with the switches beneath it. Makes the relationship true and finally shows what a theme looks like on the shelf. Costs a two-column gallery — six themes in view instead of nine, over a thirty-theme catalogue. |

Related: `../couch-settings-v5/` (round 5, shipped as PR #234), `../couch-settings-v4/` (Emulators,
shipped as PR #228).
