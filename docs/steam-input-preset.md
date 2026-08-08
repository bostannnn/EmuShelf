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

## Install the bundled template

EmuShelf bundles a verified controller layout — **"EmuShelf — Hotkeys for emulators"** — and
**Settings → Hotkeys → Install Steam Input template** copies it into Steam's
`controller_base/templates/` folder. After that, open the emulator's controller settings in Steam,
choose **Browse Configs → Templates**, and pick **EmuShelf**.

Caveats:
- The bundled layout targets a **DualSense (PS5)** controller; other controller types would need their
  own variant. It implements the table above.
- Steam exposes no clean API to *activate* a config for an app, so this installs a selectable
  **template**, not a per-game binding — you still pick it once per emulator.
- On the desktop the emulator must be **launched under Steam** (add it as a non-Steam game) for Steam
  Input to apply; on a Steam Deck this is automatic.
- If Steam doesn't list the template, the same layout is on the Steam Workshop — search "EmuShelf".

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
