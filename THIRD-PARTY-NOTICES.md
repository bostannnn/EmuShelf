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

## Archive extraction for the emulator install manager

EmuShelf uses the managed [`SharpCompress`](https://github.com/adamhathcock/sharpcompress) package to
unpack the `.7z` and `.tar.xz` archives some emulators publish (e.g. PCSX2, RPCS3), when the user
installs an emulator through the in-app install/update manager. `.zip` and `.AppImage` archives use the
framework's built-in reader and a chmod; macOS `.dmg` images use the system `hdiutil`. SharpCompress is
used read-only, to extract a user-initiated download into the portable `Emulators/` folder.

- Copyright: Copyright (c) 2014 Adam Hathcock.
- License: MIT; the complete text is retained in
  `src/EmuShelf.App/Assets/ThirdParty/SharpCompress-LICENSE.txt`.

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
