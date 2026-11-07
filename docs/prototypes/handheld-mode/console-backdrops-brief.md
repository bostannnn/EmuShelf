# Brief: a top-screen backdrop for every console in EmuShelf's Handheld mode

Written 2026-11-07 for whoever takes this on next. Two rounds of my mockups were rejected on visual quality; the
constraints, the owner's feedback and the plumbing below are all still valid. Start from the owner's words, not
from my pictures.

## What this is

EmuShelf is a C#/Avalonia game-library frontend. **Handheld mode** (milestone M45, spec in `HANDHELD-MODE.md`) targets
the AYN Thor, a dual-screen Android handheld: bottom screen 1240×1080 is the **Launcher** (a 5×4 grid of folders
and games), top screen 1920×1080 is the **Stage**. The Stage shows whatever is focused on the Launcher, large and
centred: a platform **folder** (a rounded two-tone folder with three covers peeking out, ~450 px square) or a
**game** as its physical medium (cover/case), with a white **name plate** along the bottom (~560 px wide, 64 px up
from the bottom edge) and a small status pill top-right. Behind all that is the **backdrop**. That backdrop is
what this brief is about.

Today the backdrop is the **"era motif"** (`src/EmuShelf.UI/Views/Handheld/Motion/HandheldEraBackdrop.cs`, commit
f6af72c): a light tint in the system's accent colour with either a faint pixel grid plus drifting blocks
(cartridge systems) or scanlines plus a rolling light bar (disc systems). The owner's verdict on it: the pixels
did not read as pixels, and everything looked too empty.

## The owner's direction, in their own words

- Round 1 (era motif, as built): "the cartridge pixels did not read as pixels and the disc scanlines were fine but
  everything felt too empty."
- Round 2 (abstract options in the system hue): liked "Dither sky", "8-bit horizon" and "Cover wall on a CRT".
  Then: a **unique backdrop per console, true to that generation of games.**
- Round A of the per-console lab (tinted textures: a Mode 7 floor, a four-shade overworld, line-scroll bands, a
  cover wall, bloom, light shafts…): nearly all **no** or **maybe**. Notes: DS "How is this even connected to DS?";
  PS1 "good idea to use ps1 kind of graphics, but need polishing"; PS2 "Any other ideas beside covers?"; PS3
  "Animation close to XMB is nice".
- Then: "something that will better resemble each console generation (maybe use graphics similar to biggest
  games of each platform?)"
- Round B (full scenes in each era's palette: brick platformer, kart circuit, GBC route, chequered hills, low-poly
  valley, dunes…): rejected on visual quality.

Read that as: the owner wants backdrops a player recognises **instantly** as "that console's games", and they
want them to look **good**, like real game art of the period, not like a programmer's approximation of it. Both of
my rounds were procedurally drawn in JavaScript from primitives (circles, rects, sine ridges). That is likely the
ceiling I hit: the compositions and pixel work were not at the level of actual game art. A different approach
(hand-authored pixel art or illustrations, an image model, or a real artist's assets) is probably what is needed.

## Hard constraints

**Originality / IP.** Original art only. No boot screens, BIOS or system-menu looks (XMB, Wii channels, the
Dreamcast swirl, the GameCube cube, DS/3DS home menus), no logos or wordmarks, no characters, no art, sprites,
tiles or level layouts taken from games. Borrow the *visual language* of a generation (palette limits, tile
grids, Mode 7 perspective, dithering, cel shading, low-poly, bloom), not any one game's assets. No OpenEmu
branding or unlicensed artwork (CLAUDE.md). Anything third-party needs a verified licence and credit in
`THIRD-PARTY-NOTICES.md`.

**Performance on the Thor.** This is the one that bites. An earlier version ran a 120 Hz compositor loop on a
full-screen layer and the fans ramped. The M45 session has since fixed `HandheldEraBackdrop` so that:
- motion is stepped in whole pixels a few times a second (target ≤ 6 steps/s; 0 is fine),
- motion stops while the Stage is hidden or the app is in the background,
- at most one crossfade runs at a time, and a crossfade happens only when the **system** changes (moving between
  games of one system changes nothing).

So a backdrop must be **pre-rendered layers that are only translated, rotated or swapped** (opacity on/off between
pre-drawn frames). No per-frame blur, no per-frame redraw, no shaders. Think: a handful of bitmaps, a
`TranslateTransform` or `RotateTransform` each, a `DispatcherTimer`. Rough budget I used and nobody objected to:
≤ ~8 MB of bitmaps per backdrop, ≤ ~30 moving layers, pixel art stored at native low resolution and scaled by an
integer with nearest-neighbour, smooth art stored at half resolution.

**Composition.** The folder/medium sits dead centre (roughly x 735–1185, y 236–684 for the folder) and the name
plate covers roughly x 680–1240, y 870–1016. Keep that centre calm and the art readable around it; the Stage is
meant to stay fairly **light** so the artwork and plate carry. (The owner did not object to one dark backdrop for
Arcade.) The whole thing is 1920×1080, filled, never letterboxed.

**Rules of the house** (CLAUDE.md, HANDHELD-MODE.md): transform and opacity only, pre-rendered shadows and blur,
MVVM, platform-specific behaviour behind interfaces in Core.

## The systems

From `src/EmuShelf.Integrations/Systems/KnownSystems.cs` (id, name, accent colour, cover ratio w:h):

| id | Name | Accent | Cover | Notes |
|---|---|---|---|---|
| nes | Nintendo Entertainment System | #9B7E6B | 0.72 | cartridge |
| snes | Super Nintendo | #8D66C4 | 1.434 | cartridge |
| gbc | Game Boy Color | #4FAE9C | 1.0 | cartridge |
| gba | Game Boy Advance | #7065A7 | 1.0 | cartridge |
| gamecube | GameCube | #7B68C9 | 0.708 | disc |
| nds | Nintendo DS | #7580B9 | 1.115 | card, dual screen |
| wii | Wii | #49B3C9 | 0.708 | disc |
| 3ds | Nintendo 3DS | #C0568E | 1.129 | card, dual screen |
| megadrive | Mega Drive / Genesis | #3A6D74 | 0.708 | cartridge |
| dreamcast | Dreamcast | #F07C3E | 1.0 | disc |
| playstation | PlayStation | #8A8FA3 | 1.0 | disc |
| playstation2 | PlayStation 2 | #3D6DB5 | 0.708 | disc |
| psp | PSP | #596EBC | 0.581 | UMD |
| playstation3 | PlayStation 3 | #2E3A87 | 0.708 | disc |
| arcade | Arcade | #C0473A | 1.333 | boards; covers are title screens |
| steam | Steam | #66C0F4 | 0.667 | digital; a generic modern look is fine |

The accent colour tints the folder, the selector glow and (today) the backdrop. The backdrop does **not** have to
use it; round B used each era's own palette and nobody objected to that part.

## Deliverable

1. **A lab the owner can judge from.** An HTML page at 1920×1080 switchable per system, with a short note per
   option: what it borrows from and what it costs on the Thor (layers, moving layers, steps/s, bitmap bytes).
   The existing lab, `docs/prototypes/handheld-mode/console-backdrops-lab.html`, already has the harness:
   system chips, folder/game toggle, Keep/Maybe/Not-this per system with notes saved in `localStorage`, "Copy
   picks", a contact sheet, a layer engine (`fill`, `tile`, `still`, `drift`, `sway`, `flip`, `spin`, `blink`)
   that mimics what the app can do cheaply, and a cost readout. Reuse it or replace it; the owner knows how to
   use it. If you author bitmaps rather than generate them, embed them as data URIs or publish them alongside.
   **Never commit or publish the owner's real covers**; generated placeholders only in the repo. Real covers can
   be loaded locally with `?covers=http://127.0.0.1:8765` while serving the round-2 notes folder (see below).
2. **The owner's picks**, one backdrop per system or a shortlist.
3. **An implementation plan** for `HandheldEraBackdrop`. The one already in the notes folder
   (`per-console-backdrops.md`) still holds for the plumbing: replace `GameSystem.Era` / `SystemEra` with a string
   `BackdropId` per system in `KnownSystems`, pass it through `HandheldPlatform` and `HandheldBackdrop` (keep value
   equality so the crossfade fires only on a real change), a small registry of `IStageBackdrop` builders, one
   stepped `DispatcherTimer` per visible backdrop, bitmaps built once and cached per id, a unit test that holds
   every backdrop to the budget. Update the picked ids and the asset approach (generated vs authored bitmaps).

Commit the lab locally on the branch (design work: **no PR**). Don't touch `src/` in the design phase.

## Where things are

- Branch `claude/interesting-fermat-c728ba`. Commits: cb65c4b (round A lab), f845cf6 (round B). Both rejected,
  both still in the file as reference for what *not* to do, and for the harness.
- Lab: `docs/prototypes/handheld-mode/console-backdrops-lab.html`. Serve `docs/prototypes/handheld-mode/` with
  `python3 -m http.server 8771` (there is a `.claude/launch.json` entry, `handheld-labs`).
- Private Artifact of the lab (owner only): https://claude.ai/artifact/W4M7Ej4tF75x9Kan9HAYz3
- Earlier folder-motion lab (openings and round-1 backdrops): `docs/prototypes/handheld-mode/folder-motion-lab.html`.
- Spec: `HANDHELD-MODE.md` ("Top screen — the Stage", and the numbers table). Decisions: `DECISIONS.md`, entries
  "M45 H3" and "M45 H4" (the H4 entry describes how the current backdrop is drawn and why).
- Current code: `src/EmuShelf.UI/Views/Handheld/Motion/HandheldEraBackdrop.cs`, `HandheldItems.cs`
  (`HandheldPlatform`, `HandheldBackdrop`), `HandheldShellViewModel.cs` ~line 1060 (where the backdrop is set),
  `src/EmuShelf.Core/Systems/GameSystem.cs` (`Era`).
- Shared notes (outside the repo): `~/Library/Application Support/Claude/side-session-notes/
  local_9f8bc617-12b3-4be2-87cd-e9a115c0c144/` holds `per-console-backdrops.md` (round A verdicts, round B list,
  implementation plan) and `backdrops-round2/` (the round-2 mockup and the owner's real NES/PS2 covers; serve it
  on port 8765 for local review only).

## Suggestions I'd make to the next person

- Decide the **asset strategy first**: procedural drawing in code has now failed twice on quality. Options that fit
  the constraints: hand-authored pixel art and flat illustrations (small, integer-scaled, trivially cheap);
  image-model output used as a *still* layer with one or two small moving elements; or a short style guide per
  console and a real artist. Whatever it is, the runtime only ever translates/rotates/swaps bitmaps.
- Show the owner **two or three directions for one console** (say NES and PS2) at full quality before doing all
  sixteen. Round A and B each did all sixteen at once and both missed.
- Motion is optional. A beautiful still beats a mediocre scroller. If something moves, make it one thing, slow,
  stepped.
- Keep the "borrows from / avoids" note per option; the owner reads them.
