# Third-party notices

EmuShelf is GPL-3.0 software. The following independently licensed components
are distributed with the application under their original terms.

## OpenEmu platform-library artwork

EmuShelf includes the small platform-library icons and collection icons from the
OpenEmu project. They are used for console navigation and missing-metadata states;
OpenEmu branding, controller illustrations, emulator code, cores, and game artwork
are not included.

- Source: [OpenEmu/OpenEmu](https://github.com/OpenEmu/OpenEmu) and the
  [OpenEmu-Silicon](https://github.com/nickybmon/OpenEmu-Silicon) distribution
  supplied for this project.
- Copyright: Copyright (c) 2024, OpenEmu Team. All rights reserved.
- License: BSD 2-Clause; the complete text ships at
  `ThirdParty/OpenEmu/LICENSE.txt` and is retained in
  `src/EmuShelf.App/Assets/ThirdParty/OpenEmu/LICENSE.txt`.

The OpenEmu Team and its contributors created and maintained the original library
experience and artwork. EmuShelf is an independent project and is not affiliated
with or endorsed by OpenEmu.

## RetroAchievements rcheevos-compatible hashing

EmuShelf's read-only local game identification is a C# implementation of the
disc-hashing behavior and test vectors documented by the RetroAchievements
[`rcheevos`](https://github.com/RetroAchievements/rcheevos) project. The initial
compatibility baseline is commit `2ac45d357bce2906bb0f1438f3eaf8ce6e78e3c4`.
No native rcheevos binary is bundled.

- Copyright: Copyright (c) 2018 RetroAchievements.org.
- License: MIT; the complete text ships at
  `ThirdParty/RetroAchievements/LICENSE.txt` and is retained in
  `src/EmuShelf.App/Assets/ThirdParty/RetroAchievements/LICENSE.txt`.

## CHD (Compressed Hunks of Data) decoding

EmuShelf's read-only `.chd` decoder is a C# port of the CHD v5 container format —
the Huffman-coded hunk map, crc16 self-check, and the `zlib`/`lzma`/`cdzl`/`cdlz`/`cdfl`
hunk codecs with CD frame reassembly — used only to read a disc's boot serial. It is
derived from the MAME CHD implementation and the
[`libchdr`](https://github.com/rtissera/libchdr) reference port. No MAME or libchdr
binary is bundled.

- Copyright: Copyright (c) MAME contributors; libchdr Copyright (c) 2018
  Christopher Hindefjord and contributors.
- License: BSD-3-Clause.

The LZMA hunk decoder is a minimal C# implementation based on Igor Pavlov's
[LZMA SDK](https://www.7-zip.org/sdk.html) reference decoder.

- License: public domain (the LZMA SDK is placed in the public domain by its author).

## Zstandard decoding for Dolphin RVZ images

EmuShelf uses the managed [`ZstdSharp.Port`](https://github.com/oleg-st/ZstdSharp) package only
to decode the standard Zstandard-compressed chunks in read-only Dolphin RVZ images. It is a C#
port of Zstandard; no native binary is bundled.

- Copyright: Copyright (c) 2021 Oleg Stepanischev.
- License: MIT; the complete text is retained in
  `src/EmuShelf.App/Assets/ThirdParty/ZstdSharp-LICENSE.txt`.

## FLAC decoding for CD CHD images

EmuShelf uses the managed `Shamisen.Codecs.Flac` 0.1.0-alpha.0.8.0 package and its
`Shamisen.Core` dependency solely to decode the `cdfl` FLAC payload in read-only CD CHD
images. No native decoder is bundled. The package is MIT-licensed and includes ported
libFLAC and Intel Intelligent Storage Acceleration Library source under BSD-3-Clause terms.

- Copyright: Copyright (c) 2022 MineCake1.4.7; libFLAC Copyright (c) 2000-2009 Josh
  Coalson and (c) 2011-2016 Xiph.Org Foundation; Intel ISA-L Copyright (c) 2011-2017
  Intel Corporation.
- License: MIT and BSD-3-Clause; complete notices are retained in
  `src/EmuShelf.App/Assets/ThirdParty/Shamisen.Codecs.Flac-LICENSE.txt`.

## YAML parsing for the read-only RPCS3 game list

EmuShelf uses the managed `YamlDotNet` 18.1.0 package to read the selected RPCS3
`games.yml` file. It only parses the explicitly supported title-id-to-path mapping;
EmuShelf never writes that file or any other RPCS3 data.

- Copyright: Copyright (c) 2008-2014 Antoine Aubry and contributors.
- License: MIT; the complete text is retained in
  `src/EmuShelf.App/Assets/ThirdParty/YamlDotNet-LICENSE.txt`.

## SDL2 for native controller input

EmuShelf bundles the native Simple DirectMedia Layer 2 (SDL2) shared library to read physical
game controllers directly (via its GameController API), so Gamepad mode works without relying on
Steam Input. Only the native binary is distributed — per platform, `SDL2.dll` (Windows),
`libSDL2.so` (Linux), or `libSDL2.dylib` (macOS), for x64 and arm64. The binaries are obtained from
the [`ppy.SDL2-CS`](https://github.com/ppy/SDL2-CS) native package; EmuShelf uses its own minimal
P/Invoke and does not ship the managed SDL2-CS binding assembly. EmuShelf calls into SDL2 only for
controller polling.

- Copyright: Copyright (C) 1997-2025 Sam Lantinga.
- License: zlib; the complete text is retained in
  `src/EmuShelf.App/Assets/ThirdParty/SDL2-LICENSE.txt`.

## Opt-in network metadata sources (not distributed)

EmuShelf can, only after the user opts in, request title catalogs and individual
cover files from the following third-party projects. Their databases and game
artwork are not included in EmuShelf packages. Downloaded files remain subject to
the source project's terms and to the rights of their respective publishers.

- [libretro-database](https://github.com/libretro/libretro-database) supplies the
  cached title/identifier catalogs. EmuShelf uses its Redump serial catalogs for PlayStation 3
  and PSP, its Redump data-track SHA-1 catalog for Dreamcast, and its No-Intro SHA-1 catalogs
  for Mega Drive / Genesis, Nintendo DS, Game Boy Advance, and Super Nintendo (as well as the
  existing profiles). The database repository declares
  [CC BY-SA 4.0](https://github.com/libretro/libretro-database/blob/master/LICENSE).
- [libretro-thumbnails](https://github.com/libretro-thumbnails) supplies individual
  named box-art files when an exact canonical title is available, including the PlayStation 3,
  PSP, Mega Drive / Genesis, Nintendo DS, Game Boy Advance, Super Nintendo, and Dreamcast
  repositories. The thumbnail server is updated periodically from those repositories. No
  thumbnail or catalog is bundled; downloaded
  cover images remain subject to their respective publisher rights and source terms.
- [xlenore/psx-covers](https://github.com/xlenore/psx-covers) and
  [xlenore/ps2-covers](https://github.com/xlenore/ps2-covers) supply individual
  PlayStation and PlayStation 2 covers addressed by product code.
- [GameTDB](https://www.gametdb.com/) supplies individual GameCube and Wii covers
  addressed by disc id (`https://art.gametdb.com/wii/cover/<region>/<id>.png`), the same
  community source Dolphin uses. Cover images remain subject to GameTDB's terms and to the
  rights of their respective publishers.
- [DuckDuckGo Images](https://duckduckgo.com/) supplies search-result links only when a user
  explicitly opens **Set cover…** and searches. EmuShelf bundles no DuckDuckGo code or index and
  does not apply a result automatically. A selected image remains subject to its hosting site's
  terms and to the rights of its respective publisher.

## Bundled tools

- [rclone](https://rclone.org/) is bundled with EmuShelf's Windows and Linux (AppImage) packages
  to power optional cloud save sync. It is invoked as a separate, unmodified executable — EmuShelf
  does not link against it, and rclone owns any cloud OAuth token, which never passes through
  EmuShelf. rclone is distributed under the
  [MIT License](https://github.com/rclone/rclone/blob/master/COPYING); its license ships beside the
  executable at `ThirdParty/rclone/LICENSE.txt`.

## Bundled fonts

- [Exo 2](https://fonts.google.com/specimen/Exo+2), by Natanael Gama and the Exo 2 Project Authors,
  is bundled as the Gamepad (couch) mode UI font at `src/EmuShelf.App/Assets/Fonts/Exo2.ttf` — a
  single variable font covering every weight. It is unmodified, and Desktop mode does not use it.
- License: SIL Open Font License 1.1; the complete text ships at `ThirdParty/Fonts/Exo2-OFL.txt` and
  is retained in `src/EmuShelf.App/Assets/ThirdParty/Exo2-OFL.txt`.

## Bundled 3D models (couch physical-media shelf)

The couch shelf's 3D hero renders a game's physical medium as a lit object. The eight
shells are third-party models bundled inside `EmuShelf.Rendering` at
`src/EmuShelf.Rendering/Assets/*.glb`. Runtime-size and packaging-removal modifications are
documented per model below. Game artwork is supplied dynamically by EmuShelf; no game packaging
from a model download is intentionally displayed.

All eight are licensed
[CC BY 4.0](https://creativecommons.org/licenses/by/4.0/), which permits redistribution —
including in commercial and differently licensed works — provided the author is credited.
Credit is given here and the models keep their authorship metadata inside the `.glb` files.

- **Nitendo DS Cartridge Super Mario 64** — by
  [satchii_](https://sketchfab.com/stachiii_), from
  [Sketchfab](https://sketchfab.com/3d-models/nitendo-ds-cartridge-super-mario-64-2a768cba31054846817bcf2465611e21).
  Bundled as `ds-card.glb`. The download is four copies of the card laid out in a row; only the
  first is left drawable, by clearing the duplicates' mesh references. This shell keeps its label on
  the same atlas as its body, so unlike the NES model it could not be cleared by flattening a
  material: EmuShelf masks the label rectangle in the base-colour, metallic/roughness and normal
  maps, leaving none of the Super Mario 64 DS artwork — which the author's CC BY licence does not
  cover — in the build. Maps were reduced to 1024px, and canonical orientation, metric scaling and
  per-game label art are applied at runtime. Original authorship and license metadata remain
  embedded in the GLB.

  This replaced **Nintendo Ds cartridge (preset)** by
  [littlengvfx](https://sketchfab.com/littlengvfx), also CC BY 4.0, which is no longer bundled.

- **Sonic 2 Mega Drive Cartridge** — by [Naser](https://sketchfab.com/naser.ali), from
  [Sketchfab](https://sketchfab.com/3d-models/sonic-2-mega-drive-cartridge-4c453f8527384c718f652a9f04067119).
  Bundled as `megadrive-cartridge.glb`. This shell keeps its label on the same atlas as its body, so
  unlike the NES model it could not be cleared by flattening a material: EmuShelf masks the label
  rectangle in the base-colour, metallic/roughness and normal maps, leaving none of the Sonic 2
  artwork — which the author's CC BY licence does not cover — in the build. Maps were reduced to
  1024px, and canonical orientation, metric scaling and per-game label art are applied at runtime.
  Original authorship and license metadata remain embedded in the GLB.

- **NES || Cartridge || Battletoads** — by
  [dark_igorek](https://sketchfab.com/dark_igorek), from
  [Sketchfab](https://sketchfab.com/3d-models/nes-cartridge-battletoads-8aeab01fce084c3abaf3de792dda47a1).
  Bundled as `nes-cartridge.glb`. The model keeps its game label on a separate material named
  `sticker`; EmuShelf flattened that material's base-colour, metallic/roughness and normal maps to a
  blank plate, so none of the Battletoads artwork — which the author's CC BY licence does not cover
  — remains in the build. Maps were reduced to 1024px, and canonical orientation, metric scaling and
  per-game label art are applied at runtime. Original authorship and license metadata remain
  embedded in the GLB.

- **Super Nintendo Cartridge (PAL/Super Famicom shell)** — by
  [SomeKevin](https://sketchfab.com/somekevin), from
  [Sketchfab](https://sketchfab.com/3d-models/super-nintendo-cartridge-b2076d8a65d648ff99bf51ca9d5fca2a).
  Bundled as `snes-cartridge.glb`. EmuShelf neutralized the fixed placeholder label in all three
  PBR texture channels, reduced the 4096px maps to 1024px, removed six collapsed triangles,
  corrected triangle winding where it disagreed with the authored normals, and applies canonical
  orientation, metric scaling and per-game label art at runtime. Original authorship and license
  metadata remain embedded in the GLB.
- **Pokemon Cartridge (Gameboy)** — by
  [thegraphicsgeek](https://sketchfab.com/thegraphicsgeek), from
  [Sketchfab](https://sketchfab.com/3d-models/pokemon-cartridge-gameboy-7d79300f91a441d0ba520fdbd268aa5f).
  Bundled as `gba-cartridge.glb`. Despite its title this is a Game Boy Advance cartridge — it moulds
  "GAME BOY ADVANCE SP" across the shell — and it replaced an earlier GBA model that had no source
  file and so could not be regenerated. EmuShelf masks the Pokémon FireRed label in the base-colour,
  metallic/roughness and normal maps, leaving none of that artwork — which the author's CC BY licence
  does not cover — in the build. Maps were reduced to 1024px, and canonical orientation, metric
  scaling and per-game label art are applied at runtime. Original authorship and license metadata
  remain embedded in the GLB.
- **Gameboy Cartridge lowpoly** — by [Bob](https://sketchfab.com/MeBob), from
  [Sketchfab](https://sketchfab.com/3d-models/gameboy-cartridge-lowpoly-8b9728eab16c4056ac2636ae7f0f038f).
  Bundled as `gbc-cartridge.glb`. This is a grey DMG (Game Boy) cartridge; Game Boy and Game Boy
  Color share one 57 x 65 x 8mm shell, and EmuShelf maps it to the `gbc` system that covers the
  whole Game Boy line. Its label — a European Super Mario Land 2 sticker — sits on the same atlas as
  the body, so as with the Mega Drive shell EmuShelf masks that rectangle in the base-colour,
  metallic/roughness (shared with occlusion) and normal maps, leaving none of that artwork — which
  the author's CC BY licence does not cover — in the build. Maps were reduced to 1024px, and
  canonical orientation, metric scaling and per-game label art are applied at runtime. Original
  authorship and license metadata remain embedded in the GLB.
- **Hypnagogia 無限の夢 Boundless Dreams Jewel Case** — by
  [sodaraptor](https://sketchfab.com/sodaraptor), from
  [Sketchfab](https://sketchfab.com/3d-models/hypnagogia-boundless-dreams-jewel-case-20e4780167b6441fb364060c79870378).
  Bundled as `jewel-case.glb`, serving PS1 and Dreamcast. The download's base-colour maps are
  photographs of a complete retail case: the front insert, the tray inlay, the promo card, the
  spine title and a moulded "DreamStation" console mark. All of that printed area is masked to a
  flat card grey in the runtime derivative — three rectangles, one per printed map — leaving the
  clear outer plastic and the moulded hinge teeth, which are what make the shell read as a jewel
  case. EmuShelf projects the game's own cover art over the front insert at runtime. The disc was
  dropped along with its texture, the lid shut from the 25-degree product-shot pose it ships in, and
  maps reduced to 1024px. Original authorship and license metadata remain embedded in the GLB.
- **DVD/PS2/Wii case** — by [MacDrawz](https://sketchfab.com/MacDrawz), from
  [Sketchfab](https://sketchfab.com/3d-models/dvdps2wii-case-60c2e703f9764cd6885811452802b3aa).
  Bundled as `disc-keep-case.glb`. The download's base-colour map is a scan of a retail Mortal
  Kombat: Armageddon sleeve — front, back and spine — which the author's CC BY licence does not
  cover. EmuShelf flattens that map to the case's own moulded plastic colour, leaving none of that
  artwork in the build. Only the base colour is flattened: this model's normal and metallic/roughness
  maps carry the case's ribs, hinge, seams and scuffs rather than an embossing of the sleeve, and
  they ship byte-identical to the author's. Canonical orientation, metric scaling and per-game sleeve
  art are applied at runtime. Original authorship and license metadata remain embedded in the GLB.

These models are not affiliated with or endorsed by Nintendo, Sony, or any console
manufacturer, and no console manufacturer's branding is used as EmuShelf's own.

## Rendering libraries

- [Silk.NET](https://github.com/dotnet/Silk.NET) supplies the OpenGL bindings the shell renderer
  calls through. It is bindings only — the entry points are resolved from the context Avalonia
  makes current, so no native library is bundled.
  License: [MIT](https://github.com/dotnet/Silk.NET/blob/main/LICENSE.md).
- [SharpGLTF](https://github.com/vpenades/SharpGLTF) reads the bundled `.glb` shells.
  License: [MIT](https://github.com/vpenades/SharpGLTF/blob/master/LICENSE).
- [StbImageSharp](https://github.com/StbSharp/StbImageSharp) decodes the PNG textures embedded in
  those shells. It is a managed port of Sean Barrett's `stb_image`.
  License: public domain / MIT.
