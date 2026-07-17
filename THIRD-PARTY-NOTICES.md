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
the Huffman-coded hunk map, crc16 self-check, and the `zlib`/`lzma`/`cdzl`/`cdlz`
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

## Opt-in network metadata sources (not distributed)

EmuShelf can, only after the user opts in, request title catalogs and individual
cover files from the following third-party projects. Their databases and game
artwork are not included in EmuShelf packages. Downloaded files remain subject to
the source project's terms and to the rights of their respective publishers.

- [libretro-database](https://github.com/libretro/libretro-database) supplies the
  cached title/identifier catalogs. The database repository declares
  [CC BY-SA 4.0](https://github.com/libretro/libretro-database/blob/master/LICENSE).
- [libretro-thumbnails](https://github.com/libretro-thumbnails) supplies individual
  named box-art files when an exact canonical title is available.
- [xlenore/psx-covers](https://github.com/xlenore/psx-covers) and
  [xlenore/ps2-covers](https://github.com/xlenore/ps2-covers) supply individual
  PlayStation and PlayStation 2 covers addressed by product code.
- [GameTDB](https://www.gametdb.com/) supplies individual GameCube and Wii covers
  addressed by disc id (`https://art.gametdb.com/wii/cover/<region>/<id>.png`), the same
  community source Dolphin uses. Cover images remain subject to GameTDB's terms and to the
  rights of their respective publishers.
