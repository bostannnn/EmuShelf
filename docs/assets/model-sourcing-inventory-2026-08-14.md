# Supplied model inventory — 2026-08-14

Measured, not eyeballed, from the files in the user-supplied `models/` area. This exists so the next
platform pass starts from numbers instead of re-inspecting the supplied GLBs. Nothing here was
integrated when it was written; only SNES and the keep case were shipped shells then.

## Licensing

All of them carry `asset.extras` with author, title, source URL and **CC BY 4.0**. That satisfies
EmuShelf's redistribution gate provided each adopted shell is credited in `THIRD-PARTY-NOTICES.md`.

**But four of them have a specific game's label art baked into their base-colour map** — Super
Mario 64, Pokémon, Sonic 2, Battletoads. The modeller's CC BY licence covers the modeller's work; it
grants nothing in Nintendo's, Sega's or Rare's artwork. Each of those shells needs the same
placeholder-neutralisation `SnesModelPrep` performs before it can ship, exactly as recorded in the
SNES review. The keep case is the only one with no game art on it.

## Per model

| Folder | Author | Tris | Maps | Proportions (W:H:D) | Notes |
|---|---|---|---|---|---|
| `snes` | SomeKevin | 33,839 | 3 x 4096 | 1.665 : 1 : 0.257 | Ships today. Reproduces the runtime asset byte-for-byte. |
| `ps2` | MacDrawz | 4,288 | 3 x 4096 | 0.695 : 1 : 0.072 | Ships today as `disc-keep-case`. No game art. |
| `nes` | dark_igorek | 1,266 | 3 x 2048 + 3 x 1024 | 1.124 : 1 : 0.135 | Proportions match a real NES cart (1.125) almost exactly. Two material sets. |
| `gba` | thegraphicsgeek | 5,802 | 3 x 4096 | 1.748 : 1 : 0.200 | Ships as `gba-cartridge`. Was filed under `gbc/` when this inventory was written, and is a GBA cartridge: its mesh is named `GBA_SP_Cartridge`, and a GB/GBC cart is taller than wide (~0.88), not 1.75. Moved to `gba/` when the real Game Boy shell arrived. |
| `gbc` | Bob (MeBob) | **510** | 3 x 2048 | 0.885 : 1 : 0.140 | Ships as `gbc-cartridge`. The replacement for the GBA cartridge above, and correctly shaped this time (0.885 against a real 57 x 65mm cart's 0.877). A DMG cartridge rather than a Game Boy Color one — grey, no clear shell — which is accepted because the two share a shell and `gbc` covers the whole Game Boy line. Lower-poly than the Mega Drive shell this document warns about, but it does not fail the same way: its chamfered corner, moulded ridges and label recess are real geometry, and it holds up under the studio key. |
| `genesis` | Naser | **334** | 3 x 2048 | 1.553 : 1 : 0.108 | Far too low-poly for this studio: no bevelled edges to catch the key, so it will read as a flat printed card. Fails the design's asset gate on silhouette. |
| `ds` | satchii_ | 20,016 | 3 maps | see notes | **Four identical copies** of one card in a single file, laid out in a row by node matrices. Each card is 33.7 x 35.1 x 1.8, i.e. lying flat (thickness on Y, so it needs a canonical rotation) and about half the real relative thickness of a 3.8mm DS card. |

## Reproducibility

`dotnet run --project tools/EmuShelf.Rendering.Preview -- --prepare-snes models/snes/super_nintendo_cartridge.glb --prepare-out <out.glb>`
reproduces `src/EmuShelf.Rendering/Assets/snes-cartridge.glb` byte-for-byte (SHA-256
`6c0825db…8d11`, 3,474,256 bytes). It needs no GPU and returns before any GL setup, so it runs on
macOS. Keep these sources: without them the shipped derivative cannot be regenerated or corrected.

`dotnet run --project tools/EmuShelf.Rendering.Preview -- --prepare-model models/gbc/gameboy_cartridge_lowpoly.glb --prepare-out src/EmuShelf.Rendering/Assets/gbc-cartridge.glb --neutral-rect 0.5100,0.1690,0.8480,0.4885 --neutral-fill 696969 --max-texture 1024`
reproduces `gbc-cartridge.glb` byte-for-byte (SHA-256 `c8fb6323…e365`, 3,280,136 bytes). The
rectangle is the Super Mario Land 2 sticker's UV bounds and the fill is the shell's own plastic
grey; `--dump-atlas` on the *prepared* file is the check that the mask covered it.
