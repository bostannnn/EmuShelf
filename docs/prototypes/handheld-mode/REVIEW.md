# Handheld mode review: why it feels unfinished, and a DS/3DS/Wii direction

Date: 2026-11-06. Written against mockup version 4 (`index.html`), `HANDHELD-MODE.md`, the
round-by-round log in `README.md`, and the Cocoon reference photos.

## The short version

The mockup is a **picture of a home screen**, not a home screen. Everything you can *do* to it
(make a folder, name it, fill it, decide what A does to a game) is either hidden behind a
sub-menu of a sub-menu or not designed at all. That is why the folder question came up: the spec
has an answer (platform folders are automatic, your own folders come from Y → Add to folder… →
New folder), but nothing on screen tells you so, and the eighteen empty slots on home do nothing
when you press A on them.

The visual list another agent gave (thin top screen, placeholder art, generic identity, untested
motion, missing screens) is right, but it is the second layer. Fixing the pictures first would
give a prettier picture of a home screen. The things to fix first are the **rules of the place**:
what lives where, how things get there, and what pressing A commits you to.

The good news: more of the "deferred" art is already in the app than the spec says. Fan art and
title logos are scraped, stored and already drawn in Gamepad mode's spotlight hero
(`MainViewModel` picks `GameMediaKind.Fanart` and `GameMediaKind.Wheel`), and the artwork
palette engine that recolours Gamepad mode from the focused cover is built and tested. The
Cocoon-style top screen is reachable with existing data.

## Part 1 — The missing core logic

These are the rules a 3DS or Wii owner takes for granted. None are written down or visible yet.

### 1. Home is a *place with slots*, not a list

On the 3DS, the HOME Menu is a fixed grid where empty slots are visible as faint tiles. That one
detail is what makes it feel like a place you arrange rather than a list that happens to end.

- Draw every empty slot faintly. The selector can land on them.
- **A on an empty slot = "New folder here."** This is the 3DS gesture (touch an empty space to
  create a folder) and it is the discoverable path. It answers "how do I make a folder" without a
  menu.
- Pages are fixed-size (20 slots). A new page appears when the last one fills, as on the 3DS.
  The page label ("Page 2 of 3") sits with the dots.

### 2. Three tile kinds, one rule each

| Tile | Where it comes from | Can I delete it? | What "Remove" means |
|---|---|---|---|
| Platform folder | Automatic, one per system with games. Always complete. | No. It leaves when the platform has no games. | Not offered. |
| Your folder | You make it. | Yes. Games stay in the library and in their platform folder. | "Delete folder" with a confirmation that says the games are kept. |
| Game on home | You pinned it. | Yes. | "Remove from home". The game stays in the library. |

The important decision hiding here: **a game can be in several places at once.** It is always in
its platform folder, and may also be pinned to home and in any number of your folders. Your
folders and home pins are references, not moves. This is the iOS/Android model rather than the
3DS one (where a title lives in exactly one place), and it is the right one for a library,
because the platform folder must stay complete. Write it into "Fixed product decisions" and make
every "Remove" verb say which of the three it is. Today's Gamepad "Remove from library" must stay
a separate, clearly worse-sounding action.

### 3. Three ways to make a folder, one flow behind them

1. **A on an empty slot** (the discoverable one, above).
2. **In move mode, drop a game onto another game.** The two become a folder, as on phones.
3. **Y → Add to folder… → New folder** (what the mockup has).

All three land in the same flow: the folder appears on home at that slot, the keyboard opens on
the bottom screen, and the top screen shows the folder with the name updating as you type. This
is what the 3DS does when naming a folder, and it is the first time the dual-screen pairing
earns its keep for input. Then colour, which can also be a chip on the keyboard screen.

### 4. A on a game should *not* launch

This is the biggest missing screen, and the most Wii/3DS decision in the list. Clicking a Wii
channel does not start it; it zooms into the channel's banner page with a **Start** button.
Selecting a 3DS title shows its banner on top and asks you to open it. That pause is where the
console tells you what you are about to do.

EmuShelf needs it more than Nintendo did, because launching means starting an external emulator,
possibly after a disc chooser, a screen chooser (built-in or external), a save-sync, and a
missing-file check. Today those are modal overlays stacked on a launch that already started.

Design a **Title page** instead:

- Bottom screen: the cover, big, with a row of three buttons: **Play**, **Disc 1 ▾** (only when
  there is more than one), **Achievements**. Below: last played, play time, platform, the emulator
  it will use. A = Play. Y = the full actions sheet (edit title, cover, scrape, remove). B = back
  to the grid.
- Top screen: fan art full-bleed with the logo, or the cover on a tinted backdrop when there is no
  fan art. This is where the Cocoon frame comes from.
- Play from here runs today's launch flow. The screen chooser and save-sync panel become states of
  this page, not overlays on top of the grid.
- Launch animation: the cover lifts off the bottom screen and lands on the top one, then the top
  screen fades to black for the emulator. The 3DS launch does a version of this. It is the one
  motion only a dual-screen device can do, and it should be the signature of the mode.

### 5. What happens when I open the device

The 3DS and Wii answer "what do I do now" within one press. Home in the mockup is a platform list,
which Gamepad mode already is.

- Add an automatic **Recent** folder (last twelve played, newest first), placed first by default
  and movable like any folder. Two presses of A then resumes last night's game.
- When nothing has changed focus for about fifteen seconds, the top screen drifts through recent
  games' art (Wii channels animate at rest, the 3DS banner loops). This gives the top screen a
  life of its own and is the cheapest fix for "the top screen is thin".
- Empty library: the Recent folder is absent, home shows one tile, **Add games**, and the top
  screen explains the two screens. The setup wizard runs before any of this.

### 6. Reconciliation rules, stated

The spec has these in Architecture; they belong in the decisions list because they are what the
user experiences:

- New platform → its folder is appended to the last page with a "New" ribbon until opened.
- Platform loses its last game → the folder leaves and its slot goes empty. Nothing shifts.
- Game removed from library → it leaves every folder and home pin quietly.
- Game file missing → the tile stays, dimmed, with the existing "unavailable" treatment, and the
  Title page says what is missing.

## Part 2 — What to borrow from the DS, 3DS and Wii

Ideas, not assets. Each one with the reason it works and where it lands.

| Decision | Why it works | Where it lands here |
|---|---|---|
| **Top shows, bottom controls** (DS) | One screen you look at, one you touch. Never split attention. | Already the premise. Keep every control on the bottom; the top never has a focusable element. |
| **Visible empty slots, fixed pages** (3DS, Wii's 12-channel pages) | The grid reads as a place, so arranging it feels natural. | Faint slots, A on a slot = new folder, page label. |
| **Preview before commit** (Wii banner page, 3DS title banner) | Starting software is a decision; the pause is where the system talks to you. | The Title page (Part 1.4). |
| **The launch travels between screens** (3DS) | Dual-screen motion that no single-screen launcher can do. | Cover lifts bottom → top, then fade to emulator. |
| **Live top screen at rest** (Wii channel banners, 3DS banner loop) | The top screen is never a static label. | Idle drift through recent games; folder focus shows the folder's covers fanning out. |
| **Three icon sizes** (3DS icon zoom) | People with 400 games and people with 20 want different grids. | Settings → Handheld → Tile size: Large 4×3, Normal 5×4, Small 6×5. Not in Phase 1. |
| **System bar in the corners, clock in the middle** (Wii Menu) | Status is glanceable without a status bar. Cocoon's bar is a Wii bar. | Bottom bar: round swap button left, dock centre, round menu button right. Time and battery as a small pill on the top screen, top-right, exactly where Cocoon puts it. |
| **Name while you look** (3DS folder naming) | Keyboard on the bottom, result on top. | Folder naming, title editing, search. |
| **One sound family** (Wii) | A single voice for the whole menu. | Phase 5 as planned. Five sounds, not twenty: move, open, close, confirm, back. |
| **Light, airy, white-and-blue** (Wii Menu, 3DS default) | Reads as a console, not an app. The lavender pastel reads as a template. | See identity below. |
| **Page flip with a real edge** (Wii) | The page arrows at the screen edges are both a hint and a button. | Keep the `>` edge hint, make it tappable, and give the page slide the track spring. |

What *not* to borrow: Miis, the 3DS's one-place-per-title model, the Wii's cursor (we have a
selector), and any Nintendo glyph, sound or layout proportion.

## Part 3 — The visual identity

The mockup's problem is not that it is pastel; it is that nothing in it is EmuShelf's. Three
moves, all cheap:

1. **Base it on the theme catalogue, not a new palette.** `ThemeCatalog` already has the app's
   themes with an indigo accent. Handheld mode gets a light default derived from the light theme
   (Wii-airy: near-white surfaces, cool grey chrome, the app's indigo for the selector) and a dark
   variant for free from the dark theme. That also answers "no dark launcher".
2. **Tint the whole interface from the focused game.** `ArtworkPaletteFactory` and
   `ArtworkPaletteExtractor` already do this live in Gamepad mode, with contrast floors and a
   hysteresis so scrolling doesn't strobe. Wire the Stage backdrop, the name plate and the
   selector glow to it. This is Cocoon's colour wave, done with code that exists.
3. **Use the fan art and logos that are already scraped.** Folder focus on top: the folder's
   covers fanned on a backdrop tinted from the platform colour. Game focus on top: fan art with
   the logo over it, cover as fallback. Nothing here waits on the ScreenScraper work; it waits
   on the user having scraped, which is a Settings toggle today.

Type: one rounded face is right for the mode, but Nunito everywhere is the template smell. Use it
for names and plates, and the app's existing UI face for chrome and settings, so the comfy voice
is reserved for the things you own.

## Part 4 — The screens that are missing, in the order to draw them

1. **Title page** (Part 1.4), including disc and screen choosers as states.
2. **New folder flow**: empty slot → folder appears → keyboard → colour. Rename reuses it.
3. **Empty states**: empty library, empty folder, folder with one game, a page of empty slots.
4. **Start menu** as a system page, not a list: Search, Add games, Achievements, Settings,
   Switch mode, Sounds, Quit to Gamepad mode. Six big tiles on the bottom screen, the top screen
   explaining the focused one.
5. **Settings as master-detail across the screens**: sections and rows on the bottom, the
   focused row's description, current value and preview on top. This is the DS answer to a
   settings page and it reuses every existing row kind.
6. **Search**: keyboard on the bottom, results fanning on the top, A jumps to the Title page.
7. **Confirmations** (delete folder, remove from home, remove from library) as a bottom-screen
   sheet with the top screen showing what is about to happen.
8. **Swapped layout**: when the launcher is on the 16:9 screen, use the width: 7×3 at the same
   tile size, with the page label and system bar in the extra height. Don't centre a 5×4.
9. The wizard and the scrapers last; they are the existing Gamepad overlays restyled.

## Part 5 — The motion that needs a decision, not more tuning

The spring work from rounds 3 and 4 is right and should stop being re-tuned in a browser. Two
rules are missing from the motion spec:

- **The selector never disappears.** On every transition (folder open, Title page, swap, sheet)
  the frame is visible on the first frame of the new screen. If it has to travel, it travels. If
  it has nowhere to go, the screen is wrong.
- **Nothing on the top screen moves unless the bottom screen caused it.** The idle drift is the
  one exception and it stops on the first press.

Then Phase 0 on the Thor, with the real spring constants as the output.

## Part 6 — What I would not re-open

The settled decisions hold up. Monogram folders are the right call (console art would fight the
covers inside). The dock as the real companion dock is right. One companion panel at a time is
right. The only settled item that contributes to the unfinished feel is "current cover art for
now": it is fine for tiles, but the top screen should use fan art and logos from day one because
they already exist.

## Suggested next round

Not a fifth browser round of the same screens. A round 5 that adds exactly these, in this order,
and nothing else:

1. Visible empty slots and A-on-a-slot folder creation with the naming flow.
2. The Title page and the bottom-to-top launch.
3. Fan art, logo and artwork tint on the Stage, with the status pill.
4. The Recent folder and idle drift.
5. A light Wii-airy theme from the catalogue, plus the dark variant.

Then update `HANDHELD-MODE.md`: add decisions 11–14 (multi-place games, Title page before
launch, empty-slot creation, Recent folder), move fan art and logos from "Later" into Phase 1's
Stage, and add Phase 1b "Title page" between the skeleton and the motion pass.

## Housekeeping

- `HANDHELD-MODE.md` and `docs/prototypes/handheld-mode/` are untracked in the main checkout, so
  this review, written on the `claude/handheld-mode-design-gaps-5ca17c` branch, cannot sit beside
  them in git yet. Decide whether to commit the spec and mockup first.
- The `reference/` photos show hands and a room and the repo is public. Keep them out of git;
  the written description in `HANDHELD-MODE.md` is enough for another reviewer.
