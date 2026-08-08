# Steam Input preset — controller hotkeys for EmuShelf's keyboard scheme

EmuShelf writes a uniform **keyboard** hotkey scheme into each emulator (see
`docs/hotkey-keyboard-scheme.md`). To drive those keys from a controller, do the controller→keyboard
translation **once**, outside the emulators, with a Steam Input layout. Because every emulator listens
for the same keys, one layout works for all of them.

## The layout

Use **Select** (Back / View / Create — the "share"-side button) as a hold modifier. While it is held,
map the face buttons and Start to the scheme's keys:

| Hold + button      | Key  | Action        |
|--------------------|------|---------------|
| Select + Square    | `R`  | Rewind        |
| Select + Circle    | `L`  | Fast-forward  |
| Select + Triangle  | `F2` | Save state    |
| Select + Cross     | `F4` | Load state    |
| Select + Start     | `F8` | Close game    |

(Square / Circle / Triangle / Cross are the PlayStation labels; on an Xbox pad they are X / B / Y / A by
position.)

## Why a guide, not an auto-installed file

Steam Input configurations are applied **per app** and are normally shared through the Steam Workshop,
not dropped in as files — there is no clean, supported way for a third-party app to install one for you.
So EmuShelf ships this guide rather than a config binary. On a Steam Deck this is the natural path; on
the desktop the emulator must be **launched under Steam** (add it as a non-Steam game) for Steam Input
to apply.

## Setup (desktop)

1. In Steam, **Add a Non-Steam Game** and pick the emulator's executable. Repeat per emulator — the same
   layout is reused for each.
2. Open the game's **Controller settings → Edit Layout**.
3. Make **Select/Back** activate an **Action Layer** (hold), and within that layer bind: Square → `R`,
   Circle → `L`, Triangle → `F2`, Cross → `F4`, Start → `F8`.
4. Save the layout, and launch the emulator **through Steam** so the layout is active.

## Setup (Steam Deck)

Steam Input is already in the path for anything launched from your library, so you only need the layout.
Add each emulator as a non-Steam game if it is not already there, then apply the layout above.

## ⚠️ RetroArch — verify before relying on it

RetroArch reads controllers through raw input and is known to **ignore keystrokes injected by other
software** (libretro #16438). Steam Input's emulated keystrokes are *usually* delivered as real HID
keyboard events, which RetroArch should accept — but this has **not been verified on hardware**. Test it
with zero extra setup: RetroArch already defaults to `r` / `l` / `f2` / `f4` for
rewind / fast-forward / save / load, so launch RetroArch under Steam Input and confirm Select + a face
button triggers the action. If it does not, the other emulators are unaffected — RetroArch would just
need its hotkeys driven by a real keyboard instead.
