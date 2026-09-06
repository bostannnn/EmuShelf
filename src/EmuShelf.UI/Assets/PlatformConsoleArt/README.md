# Platform console artwork

These transparent PNG assets replace generic platform icons only for their matching
PS2, PS3, Wii, and PSP system ids. `wii.png` was supplied by the EmuShelf user on
2026-07-20. `playstation2.png`, `playstation3.png`, and `psp.png` are custom
pixel-art sprites generated for EmuShelf on the same date so they remain recognizable
at sidebar size. No OpenEmu artwork or third-party game artwork was used to create
these files.

`steam.png` is original generic PC hardware pixel art generated with the built-in
image-generation tool on 2026-09-06. The existing PS2 and Wii sprites were used as
style references only. It replaces the initial flat PC/controller glyph.

Generation prompt: Create an original transparent platform hardware icon for Steam
PC games: one compact charcoal desktop tower in three-quarter perspective, shaded
pixel-art rendering matching the reference sprites, vent grids, two USB ports, a
small round power button and subtle cyan status light. Mid-gray edge highlights,
full silhouette, no text, logos, monitor, controller, cables or ground shadow.
Final edit prompt: Remove the checkerboard background, preserve the PC tower, and
output PNG RGBA with actual transparent alpha around the entire visible tower.

The final sprite was simplified following user feedback: broad shaded planes,
no vent grids, USB ports, surface texture or feet, and one cyan power light.
Edit prompt (built-in image generation): Simplify the PC tower for 32-pixel UI
readability, preserving its silhouette and three-quarter perspective; use solid
charcoal panels, restrained gray edge highlights and one cyan rectangular light.
Remove all fine detail. A final background-removal pass preserves actual RGBA
transparency.
