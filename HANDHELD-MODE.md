# Handheld Mode — a third, dual-screen interface

Status: baseline agreed (2026-11-06) after five mockup rounds and a review. Nothing is built yet.
The build plan is `ROADMAP.md` M45 (sub-milestones H0–H6); the decisions are recorded in
`DECISIONS.md` under 2026-11-06.

*Status now (2026-11-15; the line above is the original, kept):* built and shipped in M45. Its look
is being replaced by **Big Type** (`ROADMAP.md` M47; decision 16 and the motion spec below), which is
built behind a look switch whose default is still today's look.

**Handoff:** [docs/prototypes/handheld-mode/](docs/prototypes/handheld-mode/) has the interactive
mockup (`index.html`), Andrew's Cocoon reference photos, the round-by-round feedback log, an honest
list of what is unfinished, and a ready-made brief for a reviewer. "Mockup 1–4" below refers to
those rounds.

"Handheld mode" is a working name.

## Product goal

Add a third interface mode, alongside **Desktop** (OpenEmu-like library) and **Gamepad** (couch
shelf). It should feel like a handheld console's home menu (DS/3DS HOME Menu, Cocoon). Two screens
work together:

- The **bottom screen** is where you pick things: a grid of big rounded icons that you arrange.
- The **top screen** shows off whatever is selected.

The feel matters more than the feature count. It should be warm, rounded, bouncy, and responsive
to every press. Every move, open, close and swap is animated, and nothing ever just pops in.

Gamepad mode remains the TV/couch interface. Handheld mode does not replace it.

### References

- Cocoon (Android dual-screen launcher): owner screenshots and video from 2026-11-06 on an AYN Thor.
- Nintendo DS/3DS HOME Menu: for feel and motion only.

As with NeoStation in `GAMEPAD-REDESIGN.md`, **copy the ideas, not the product**. Do not use any
Cocoon or Nintendo source, artwork, glyphs, sounds, or exact layouts. Folder glyphs use EmuShelf's
own licensed platform artwork (`Assets/PlatformConsoleArt`).

## What the references show

**Cocoon home** (photos 1–3):

- Bottom screen:
  - 5×4 grid of large rounded-square icons.
  - Platform folders are a tinted folder shape with a console glyph. Apps are plain tiles.
  - A `>` edge hint means there is another page to the right.
  - Bottom bar: `X` swap-screens button on the left, a pill dock in the centre (app drawer and
    pinned apps), `Y` menu button on the right.
- Selector: a glowing violet rounded ring around the focused tile. The tile inside is slightly
  raised.
- Top screen:
  - Background is a soft pattern of diagonal rounded tiles.
  - The focused item appears very large and centred, with a name plate below (name, then a kind
    such as "Folder" or "Pod").
  - When an app is focused, a wave of that app's colour sweeps across the background.

**Cocoon inside a folder** (video):

- Bottom screen:
  - Header bar: back button, folder name, sort chip ("Manual"), list toggle. All of these are
    focusable by the selector.
  - 4×3 grid of square game tiles, which are box covers cropped to square.
  - The grid scrolls **horizontally**, a column at a time, with part of the next column peeking in
    at the edge.
- Selector: glides from tile to tile and sits on the focused tile with a glow.
- Top screen:
  - Full-bleed artwork of the focused game (title logo on art).
  - On selection change it **cross-fades** to the next game in about 200–300 ms.
  - Small overlays: an achievements badge bottom-left, a pair of round buttons bottom-right, and
    a status pill top-right.

## Fixed product decisions (owner, 2026-11-06)

1. **A third mode.** Desktop and Gamepad stay as they are.
2. **Android dual-screen only for now.** The AYN Thor is the reference device: top display 0 is
   1920×1080, bottom display 4 is 1240×1080, both 120 Hz. The mode is offered only when a
   presentation display exists. A single-screen fallback is deferred.
3. **You arrange the home screen.** As on the 3DS and in Cocoon, icons and folders can be moved
   anywhere, and folders hold games.
4. **Current cover art for now.** Tiles use the existing covers, cropped to a rounded square, and
   the top screen uses the same cover. Square icon art, hero/fanart and logos come later.
5. **Nintendo-like, animated and comfy.** This includes an animated selector, big icons, a folder
   that animates when selected and opened, an animated game change on the top screen, and one
   button that swaps the two screens.
6. **Widgets and themes are out of scope** for this work.

Added after mockup 1 (2026-11-06):

7. **No OpenEmu-style console art on folders. Start with Monogram** (owner, after mockup 2).
   Platform folders show the system's short name ("PS2", "GBA", "DS") in big rounded type. Your
   own folders show a glyph instead, so the two kinds look different at a glance. Line icon,
   Covers and Sticker stay in the mockup as alternatives.
8. **The dock is today's companion dock.** It holds five slots of real Android apps plus All apps,
   with the same pin, clear and launch rules. It has no fake Search or Achievements apps.
9. **While a game runs, the bottom screen is today's companion,** redesigned in the comfy look.
   It keeps the same features: the resting dim, achievements, photos and video, and the dock.
   Dual-screen games are the exception; see open question 2.
10. **All core features stay:** settings, emulators, save sync, achievements, scraping, adding
    games and the setup wizard. Handheld mode is a new face on them, not a reduced app.

Proposed in round 5 (2026-11-06, built into mockup 5, awaiting the owner's verdict; the reasoning
is in [docs/prototypes/handheld-mode/REVIEW.md](docs/prototypes/handheld-mode/REVIEW.md)):

11. **Home is a place with slots.** All 40 slots are drawn faintly, the selector can land on any
    of them, and A on an empty slot makes a folder there (the 3DS gesture). The other two ways,
    dropping a game on a game in move mode and Y → Add to folder…, land in the same naming flow:
    keyboard on the bottom screen, the folder and its name on the top.
12. **A game can be in several places at once.** It is always in its platform folder, and may also
    be pinned to home and in any number of your folders. Your folders and pins are references,
    never moves. Every "Remove" names the place it means, and "Remove from library" stays a
    separate, clearly different action.
13. **A on a game opens its Title page; Play is a second press.** Like a Wii channel page or a 3DS
    banner, it shows what you are about to do: cover, play data, emulator, screen, disc and
    achievements. The disc chooser and the screen chooser are states of this page, not overlays
    on a launch that already started. Launching lifts the cover off the bottom screen and lands it
    on the top one before the emulator starts.
14. **A Recent folder is made for you,** like the platform folders: the last twelve games played,
    newest first, so two presses of A resume last night's game. After fifteen seconds without
    input the top screen drifts through those games; the first press only wakes it.
    Under Big Type (M47 B15; the owner's pick of option 12, "Porthole", from
    `docs/prototypes/handheld-big-type/proposals-screens.html` on 2026-11-16) the drift keeps the console's
    backdrop: each game's console's backdrop (its circle growing from behind the window), "Recently played" over
    the game's name in big type beside a round window holding its fan art, its play time and achievements on the
    line, its console and when it was last played on the second line, no hints. The window rises through the
    hinge, floats, takes the next game's art through a circle from its bottom and drops back on a press. A game
    with no fan art is shown as when browsing (no window, no cover). **A game's logo picture is not shown** (today
    it replaces the name): the owner was told and picked without comment; it waits for their word. The timing, the
    games walked and the wake rule are unchanged.
15. **Fan art and logos on the top screen from day one.** They are not deferred work: Fanart and
    Wheel media are scraped and stored, and Gamepad mode's spotlight hero already draws them.
    The artwork palette engine tints the selector glow, the Title page and the backdrop.
16. **Look: Big Type** (owner, 2026-11-13; built in `ROADMAP.md` M47, B0 to B10). The focused
    item's name fills the Stage in huge rounded type over that console's animated backdrop; the
    Launcher's ground is the focused console's flat colour; nothing sits in a pill or a card; every
    change moves. The visual contract is the drivable mock,
    [docs/prototypes/handheld-big-type/index.html](docs/prototypes/handheld-big-type/index.html), and its
    numbers are `tokens.js` beside it, which the app mirrors name for name in `HandheldTokens`. In short:
    - **Face:** M PLUS Rounded 1c (bundled whole, OFL), 500 for text, 700 and 900 for the rest
      (`face`, `type`). It is set on the four Handheld views' roots, so every text inherits it.
    - **Colour:** one flat ground per console, approved by hand (`colour.consoles`, `Recent`,
      `Settings`, `userFolders`; `GameSystem.GroundColor`), white type on it at 4.5 to 1 or better.
      On the Stage the name, the console's name, the two lines under it, the clock and the hints
      take the ink and veil the showing backdrop needs (`legibility`, baked per backdrop in
      `legibility.json`): white, or the console's colour mixed toward `InkToward`.
    - **The backdrops** (owner, 2026-11-15, on `backdrop-review.html`; built in M47 B13): under Big Type
      each console shows **one** animation and nothing rotates: NES `nes-stars`, Super Nintendo `tiles`,
      Game Boy Color `gbc-attr`, Game Boy Advance `gba-iso`, GameCube `gc-fur`, Nintendo DS `ds-dots`, Wii
      `wii-pointer-v2`, Mega Drive `md-pipe-v2`, Dreamcast `dc-sea-v2`, PlayStation `tide`, PlayStation 2
      `ps2-orbs-v3`, PSP `psp-ribbon` (new to the app), PlayStation 3 `ps3-sheet`, Arcade `arc-vector-v3`,
      Steam `steam-mesh`, Nintendo 3DS `3ds-screen` ("Through the screen": an endless run of openings shaped like
      its top screen; the owner, 2026-11-16, of ten ideas). **Recent** has its own, `arc-checker`. Everything else
      focused without a platform (Settings, Activity, your folders, the dock and its menu, an empty slot) shows
      the **default background**, `lift-off` ("Lift-off": blocks come loose from a floor of blocks and float up
      into a spotlight), drawn in the colour the Launcher has for it; moving between two such items it stays one
      running backdrop and changes colour over the Launcher's flood. Inside a console's folder (its header, an
      app tile there) and on a game's own panel the console's backdrop stays. These two are drawn from the colour
      they are given. Where the owner chose it, an animation is **recoloured toward its console's ground**:
      the colour laid over it with CSS `mix-blend-mode: color` at the chosen pull (0.5 to 1), done in the
      backdrop's output pass (`BackdropRecolour` is the reference). Pixels are kept, crisp, where they are the
      console's character (NES, SNES, GBC, GBA, DS, Mega Drive, PlayStation, Arcade, Recent's checker: the
      console's own size in whole screen pixels, a 256-wide frame at 8 with its edges cropped); everywhere else
      the animation is drawn larger, sharp, as the Thor's GPU allows. The table, pulls and sizes are tokens.js
      `backdrops` and `BigTypeBackdrops`; the ink and veil were baked again for these (`legibility.json`
      `bigType`). Dreamcast's ground became sea blue `#0E7AB5` so its blue sea could stay as it is. Today's
      look keeps its rotating lists (`KnownSystems` `Backdrops`, M45 H7b), sizes and colours.
    - **The Stage:** the name fitted into `DisplayName.fitBox` (smaller only beside cover search's
      case), at most three lines, bottom-left; the console's name top left; battery and clock top right;
      the line and the second line under the name; the button hints bottom right as outlined circles
      (a key of two letters, RB or RS, as a rounded key). A game shows its name alone; LB or RB on a
      focused game shows its medium alone instead, the model view (decision 17, M47 B12). The app's messages (a failed launch,
      a scan's progress, what an edit did) are the line under the name, not a toast (M47 B10).
    - **The Launcher:** flat, squarer tiles (`TileHome`, `TileFolder`, `TileRadius`), monograms
      fitted to the tile, the focused folder white with its colour as ink, one white ring
      (`RingStroke`, `RingOutset`), a folder's header as its name and count in plain type, the dock
      without a bar; the Y sheet, the Start menu, Settings and the panels as rows of type on the
      colour under one ring, the focused row named on the Stage (a panel's picture, a cover or a badge,
      beside it where a case stands); one keyboard for every field, whose Delete reads Cancel with
      nothing typed so it can be left by touch, and whose ring follows a hardware keyboard's keys;
      after a game played from Search, Search comes back on the same hit (M47 B14).
    - **The empty library and setup (M47 B14):** with no games the Stage says "No games yet", A
      starts setup, or the menu's Add games. Setup is a panel ("Setup", "1 / 4"): Where are your
      games? (Add games' folder picker, then which system they are for, asked as a question of its
      own), Emulators (what will run them, Found or Not installed), Achievements? (connect opens
      Settings there when setup is done), then every folder chosen is imported while the count of
      games found climbs as the Stage's typed name, "All set", and home lands on the first console's
      folder. B goes back a question; a folder with no games or one that cannot be read says so. The
      device's own setup (storage, the second screen, closing games, each system's folders and
      emulator, saves) is today's wizard in either look, on a first start and from Settings' "Run
      setup again".
    - **Long text on the Stage (M47 B14):** the line and the second line run on at a whole word over
      three rows they share (`stage.LineRowsShared`), never an ellipsis mid-word.
    - **Behind a switch until the owner makes it the default.** The look is `HandheldLook`
      (`Classic`, `BigType`) in `EmuShelf.Core`, saved as `AppSettings.HandheldLook`, **default
      `Classic`**; `debug.emushelf.look` (`bigtype` or `classic`) overrides it for one run on
      Android, `EMUSHELF_DESIGN_LOOK` for the design renders. Today's look below is still what a
      user gets unless the switch says otherwise, and it is unchanged. The owner decided (M47 D5) that
      once Big Type is the default today's look is deleted, with no setting to choose between them;
      that has not been done yet.

    *Superseded (kept for the record):* the original decision 16 read "a light, airy white-and-grey
    default on EmuShelf's indigo accent, derived from the theme catalogue, with a dark variant from
    the dark theme. Nunito for names and plates only; the system face for chrome." That is today's
    look (`Classic`), as built in M45, without the dark variant or Nunito (neither was built: the
    views set no face and used the system's).

Added after H2 (owner, 2026-11-07):

17. **A browsed game is its physical medium, on a view of its own under Big Type** (rewritten
    2026-11-15 for M47 B12; owner: "3D models looks weird with chosen big-type style, like it is
    unnecessary", then "I like one big-type mode and separate model - approve"). The medium is the
    game's real case, cartridge or card in 3D with the cover printed on it (the Gamepad shelf's models);
    a platform without an authored medium (or a device whose GPU path fails) stands its flat cover in
    the same place.
    - **Today's look (Classic)** is unchanged: the Stage draws the focused game as its medium, resting at
      a three-quarter turn and swaying gently over a quiet wash of the platform colour; changing games
      carries on from the pose on screen; the right stick turns it and R3 swings it back. The Title page
      keeps the fan-art hero from decision 15.
    - **Big Type** never puts the medium beside the big name. A focused game has two views. The **name
      view** (the default) is the big name alone across the whole name box. The **model view**, on LB or
      RB on a focused game (in a folder, or pinned to home; the owner, 2026-11-15: "Switch the view")
      with nothing over it, is the medium
      alone in the middle of the Stage at 1.36 times its old size, its floor's shadow under it, no big
      name: the game's name is the line, and the second line is its play time, achievements and
      emulator. The hints are A Play, B Back, RS Turn, RB Name (no B on home, where it does nothing); the
      name view's gain RB Model. It rises
      through the hinge turning in, floats, holds a three-quarter turn and every fifteen seconds turns
      once all the way round so its back and spine are seen. The right stick turns it from the pose it
      has reached; letting go, or R3, swings it back. Changing games is one flip into the next medium,
      the way you moved, carrying on from the pose on screen (never a restart from rest), and in a run of
      quick presses a short flip that never stacks (M47 B11's run); a change of console changes the
      medium's kind in the same flip. On the narrow Stage (the screens swapped) the model stands in its
      middle, smaller (`NarrowModelTop`, `NarrowModelScale`), between the clock and the lines. The chosen view is kept across home and
      folders: it shows whenever a game is focused and steps aside for anything that is not one; a fresh
      start begins in the name view; the idle drift (decision 14) waits while the model shows. Over a sheet, the menu, Settings, a panel or a keyboard the
      Stage is the name view; a launch from the model view keeps the medium (it bumps, and the colour
      grows from it). Cover search (M47 B7) keeps its case beside the name, wearing the candidate. On
      home LB and RB still turn the page on anything that is not a game; with a pinned game focused the
      page turns only from the d-pad at the grid's left and right edges. The rule is one condition,
      `HandheldShellViewModel.CanSwitchGameView`: a game is focused, on home or in a folder, with nothing
      over it.

    *Superseded under Big Type (kept for the record):* decision 17 read "The Stage draws the focused game
    as its real case, cartridge or card in 3D with the cover printed on it (the Gamepad shelf's models),
    resting at a three-quarter turn and swaying gently. Changing games never restarts the motion: the
    next medium carries on from the pose on screen and settles at rest. The right stick turns it, and R3
    swings it back. Behind it is a quiet wash of the platform colour, not the blurred fan art (too busy
    behind a medium). Platforms without an authored medium keep the flat cover. The Title page keeps the
    fan-art hero from decision 15." That is still today's look (Classic); under Big Type M47 B4 to B10a
    put that medium beside the big name, which B12 replaced with the two views.

Added after the roadmap review (owner, 2026-11-08):

18. **Settings is an app on the grid.** The way into Settings is a system tile on home, as the 3DS
    and Cocoon do it, not only a row in the Start menu. The Start menu keeps a Settings shortcut and
    stays a plain list. Only Settings gets a tile for now; Search, Add games and Achievements stay in
    the menu until a tile for them earns its place. Decision 8 is unchanged: the dock still holds
    only real Android apps.

## Screen anatomy

### Bottom screen — the Launcher (default)

**Home page:**

- A **5×4 grid** of rounded-square tiles. Tiles are large: about 200 px on the Thor's bottom panel.
- More pages scroll horizontally, with a `>` / `<` edge hint and page dots.
- **Tile kinds:**
  - **Platform folder.** Created automatically, one per platform that has games. It appears and
    disappears with your library. It always contains every game on that platform, and you can
    move it anywhere. It is a folder tinted in the platform's colour, with a monogram face
    (decision 7).
  - **User folder.** Created by you: Y on a game, then Add to folder…, then New folder. It starts
    with that game in it and lands in the first free space on home. You give it a name and a
    colour, and it holds whichever games you put in. Deleting it never touches the games.
  - **Game.** A game pinned directly to home, as the 3DS allows. Shows a cropped cover.
  - **System tile.** An EmuShelf feature that looks like another app on the grid, as the 3DS's
    System Settings icon and Cocoon do it. One kind for now: Settings (decision 18). It can be moved
    and put in a folder but never removed, so there is always a way in. Its face is a glyph on the
    indigo accent, so it is neither a platform monogram nor a user-folder glyph.
- **Bottom bar:**
  - Left: `X ⇅` swap screens.
  - Centre: the dock, with All apps followed by the five pinned app slots. Down from the bottom
    row of the grid reaches it.
  - Right: the menu button, the same as pressing Start. It holds Search, Add games, Settings,
    switching modes and Sounds.

**Inside a folder:**

- **Header bar:** back, folder name, sort chip, game count. The selector can reach all of them.
- A **4×3 grid of game tiles** that scrolls horizontally a column at a time, with the next column
  peeking in.
- **Sort chip:** Manual / A–Z / Last played / Most played. Only Manual allows reordering.

### Bottom screen while a game runs — the Companion

This is today's companion, with every feature kept. It is restyled in a darker version of the
comfy look so it doesn't pull your eye from the game.

- **Resting:** after a few seconds without input it dims to a faint cover and title. A touch or any
  button wakes it, and that first press is swallowed.
- **Awake (one big panel at a time).** Two cards side by side were too small on a 3.9-inch screen
  (owner, mockup 3). From top to bottom:
  - A slim Now playing row: a small cover, title, platform and play time.
  - Two tabs, **Achievements** and **Photos**. LB/RB or a tap switches between them.
  - One large panel showing the chosen tab:
    - Achievements: a big progress ring with count and points, plus the three most recent
      unlocks. A opens the full badge grid.
    - Photos: one large screenshot. Left/right pages through them in place, and A opens the full
      viewer at that photo.
  - The dock.
  - Up and down move between the panel and the dock. The default tab is Achievements when the game
    has them, otherwise Photos.
- **Achievements:**
  - A badge grid with a detail strip.
  - Refresh follows today's cache rules.
  - New: All / Locked / Unlocked on LB/RB, taken from Gamepad mode's achievements screen.
- **Photos:** screenshots, then the video. The video plays muted and only downloads when asked.
- **New:** the D-pad reaches every part of it. Today the dock, drawer and the buttons that open
  overlays are touch-only.
- **Big Type (M47 B8, B15).** The companion is the mock's in-game screen (Now playing, the title, the Session /
  Achievements / Photos tabs, the dock row; B8). Its three layers are the owner's picks of 2026-11-16 from
  `docs/prototypes/handheld-big-type/proposals-screens.html` (B15), each on the in-game colour, the companion's parts
  dropping out under it and rising back after, its hints at the foot as plain type, each a touch button (they replace
  the round close, refresh and clear-slot buttons):
  - **All apps is the Shelf** (option 2): every app's icon on the colour in a grid like home (five across, eight on
    the 16:9 screen, four rows at a time, whole rows scrolling), the focused one grown with its neighbours stepping
    away under the one ring; its name in big type at the top, its place ("7 / 18") at the right, and over it the title
    ("All apps", "Choose an app for slot n") or what a pin did. A opens (Pin here when changing a slot), Y pins to the
    first free slot (Clear slot), B closes; a tap opens, a held finger pins, a drag scrolls. **Opened from home's dock**
    it sits on the Launcher's colour and the Stage names the focused app in big type: the title over it, where it is in
    the dock under it ("In the dock, slot 3", "Not in the dock", or what a pin did), "7 of 18 · everything on this
    device" on the second line and its hints in the Stage's corner, each a touch button (today the Stage only says
    "All apps"; new, built because the chosen option draws it).
  - **Every badge is the Wall** (option 4): all of the set's badges on the colour, seven across (eleven on the 16:9
    screen), three rows at a time; locked ones grey and dimmed, a hardcore unlock edged in gold, a hidden one a
    question mark until A reveals it; the count as a big figure top left with "Achievements · 60 pts" (or what a
    refresh is doing) over it; All / Locked / Unlocked with their counts between LB and RB (each word a touch button);
    the focused badge's name in big type under the wall, what it asks and its points and date under that; "Nothing
    here yet" where the name goes under an empty filter. Y refreshes, B closes; a tap selects a badge, a drag scrolls.
  - **The photo viewer is the Reel** (option 9): the game's pictures side by side, the one shown a fifth smaller than the
    screen (1000 × 562 on the 1240-wide screen), one either side at its edge; the place as a big figure ("2 / 6"), the
    kind beside it ("Screenshot", "Playing muted", "Loading…"), the game's title under that. Left and right slide the
    row (going round at the ends), A plays the video muted (it still downloads only then) and stops it, Y fetches
    missing artwork, B or a downward swipe closes and the Photos tab lands on that picture; a sideways swipe pages, a tap
    on the play mark plays, a tap on the picture hides the figure, the lines and the hints. The picture grows out of its
    small picture on the Photos tab and goes back into it.

### Top screen — the Stage

- **Background: the era motif** (M45 H4, picked in the folder lab). Behind a focused folder or game,
  a light field in that system's colour, patterned by the kind of media the system used:
  - **Cartridge era** (NES, SNES, Game Boy, GBA, DS, 3DS, Mega Drive, arcade boards): a vertical tint
    at about 92% → 86% lightness, a soft 22 px pixel grid of 2 px lines at 10%, a tile of large 22 px
    blocks (8 per 176×132 tile, 22%, the folder's hue and ±20°) drifting diagonally by one tile every
    22 s, and a lighter radial pool in the middle so the artwork sits on calm ground.
  - **Disc era** (PS1–PS3, PSP, GameCube, Wii, Dreamcast, Steam): a radial tint (90% at the centre,
    78% at the edges), 2 px scanlines at 9% on a 5 px pitch (2 px, not 1 px, so they do not alias on the
    1080p panel), a 140 px light bar (35%) rolling top to bottom every 24 s, and a faint vignette.
  - The era lives on the system definition (`GameSystem.Era`). A Title page without fan art keeps the
    era in the colour picked from the cover. Apps, empty slots and the menu keep the plain light gradient.
  - It crossfades over 450 ms only when the colour or era changes (moving between games of one system
    changes nothing), and only once navigation pauses for 250 ms: scrolling across home, where every
    folder is a different system, crossfades the whole screen once when you stop, not on every press.
  - Calm and low-contrast, and never blurred per frame: the grid, blocks and scanlines are tiny bitmaps
    built once per colour and tiled. The blocks or the bar are the only thing moving, stepped in whole
    pixels a few times a second (never at the panel rate), and they stop while the Stage is hidden or
    the app is in the background.
- **Focused folder:**
  - A large folder icon, centred.
  - A name plate: the name, plus "Folder · 42 games".
  - Optionally the first three covers peeking out of the folder.
- **Focused game:**
  - The cover, large, over a backdrop tinted from the cover's colours.
  - A name plate: title, plus platform and play time.
  - A compact achievements badge when the game has RetroAchievements data.
- **Focused dock app:**
  - The app's real launcher icon, large, on a coloured sweep.
  - The sweep colour is **picked from the icon itself**, like Android's Palette: white, black and
    grey are ignored, and the strongest hue wins. So it changes with every app, and a
    white-background icon still gets its brand colour. If an icon has no colour at all, a neutral
    slate is used.
  - Name plate: the app name, plus "Android app".
  - *Big Type (M47 B14):* no sweep. The mock's dock entries have no backdrop of their own, so the
    app uses the same backdrop as All apps and the menu. At home this is Lift-off in the Launcher's
    colour. Inside a console folder its backdrop continues.
  - **New logic:** today the dock rasterises icons at 96 px, which is too small for the top
    screen. Load them at about 512 px (adaptive icons scale cleanly), pick the colour once per app,
    and keep it alongside the dock data.
- **Focused All apps:** a small panel of nine app icons.

## Controls

| Input | Home | Inside a folder | Move mode |
|---|---|---|---|
| D-pad / left stick | Move selector (edges page left/right) | Move selector (columns scroll) | Carry the lifted tile |
| A | Open folder / launch game / open system tile | Launch game | Drop here |
| B | — | Close folder | Cancel; tile flies back |
| X | Swap screens | Swap screens | — |
| Y | Tile actions sheet | Game actions sheet | — |
| Start | System menu | System menu | — |
| LB / RB | Previous / next page; Big Type, on a pinned game: the name view or the model view (decision 17, M47 B12), and the page turns only from the d-pad's edges | Big Type, on a game: the name view or the model view (decision 17, M47 B12); otherwise nothing | Previous / next page |
| Right stick / R3 | — | Turn the medium / swing it back (today's look always; Big Type in the model view) | — |
| Touch | Tap = select, tap again = open; swipe = page; long-press = move | Same | Drag |

**Tile actions (Y):**

- **Every action Gamepad mode has today:** Launch, Select disc, Achievements, Edit title, Set cover,
  Scrape with ScreenScraper, and Remove from library (with its confirmation).
- **New:** Move, Add to folder…, Pin to home / Remove from home.
- **Folders only:** Rename, Colour, Delete folder. Deleting a folder never touches its games.
- **Dock slot:** Open, Change app…, Clear slot. A long press does the same thing, as it does today.

Removing a tile only changes EmuShelf's home layout. It never removes the game from the library,
and never touches its files.

## Motion spec (the heart of this mode)

There are two motion specs while the look switch exists. **Big Type's** (M47, first below) is the
one the mode is moving to; it runs only when `HandheldLook` is `BigType`. **Today's look's** (M45,
after it) is what `Classic`, still the default, runs, unchanged; it is superseded by Big Type's and
goes when today's look is removed (M47 D5).

### Big Type (M47, behind the look switch)

**Where the numbers live.** Every duration, distance, scale and curve is a named value in
`docs/prototypes/handheld-big-type/tokens.js` (`motion`, `shape`), mirrored in the app: the maths
is in `EmuShelf.Rendering/Stage/BigType` (`BigTypeMotion`, `BigTypeLauncherMotion`,
`BigTypeListMotion`, `BigTypeFolderMotion`, `BigTypeLaunchMotion`, `BigTypeTypingMotion`,
`BigTypeMoveMotion`, `BigTypeModelMotion`), pure functions of time shared by the two native compositors
(`StageCompositor`, `LauncherCompositor`) and the Avalonia views, and held to tokens.js by tests.
A number the mock does not have is marked as the app's own where it is defined. Below, a name in
code font is a tokens.js entry.

**Rules.**
- **Two curves.** Anything arriving lands on `Land` (a spring-like overshoot); anything going
  leaves on `Leave`, always quicker than it arrived. A few moves keep the mock's own curve (`Flood`,
  `Wipe`, `Scroll`, `Swap`, `StepBack`, `Launch`).
- **Timed paths, not springs.** Each move has a fixed length and hits its numbers by construction,
  so it can be tested at fixed moments. A move that comes mid-flight starts from where the thing
  was last drawn, so a held direction never jumps.
- **Transform and opacity, and a few circles.** The flood and the wipe are a circle uniform in two
  fragment shaders; the name's letters are one atlas per name, one quad per letter; rolling text
  cuts the pictures the app already has. No per-frame blur.
- **At rest, nothing is posted.** The ring's breath, the focused tile's bob, the idle hop and a
  held tile's rock live for a minute after the last press or touch and then ease out; for that
  minute the native Launcher steps at 30 Hz with nothing else moving, and after it posts nothing,
  as the Stage's backdrop rests (M45 H7).

**The moves.**

| Move | What happens | Tokens |
|---|---|---|
| The ring | Covers the old and new target by 38% of its run, lands by 74% (squashed across its travel when they are the same size), changing size and radius on the way; on another home page it lands and pops in; at rest it breathes | `Ring`, `RingRadiusTile`/`Dock`/`Row`/`Key`, `RingPop`, `RingBreath` |
| Focus | The focused tile lifts; the up-to-eight around it stand off; the focused tile bobs | `FocusLift`, `FocusScale`, `DockFocusScale`, `KeyFocusScale`, `NeighbourPush` |
| The idle hop | A wave over the tiles in view 4.8 s after a press, then every 6.5 s; the name's letters hop after the tiles | `IdleHop` |
| The name | Arrives letter by letter from the way you moved (into a folder from the hinge, out of one, or rising); the old name leaves the other way | `LetterIn`, `LetterOut` |
| The flood | The Launcher's new ground grows as a circle from the focused tile | `Flood` |
| The wipe | The Stage's new backdrop grows through a circle from below the focused tile's x, its text in its own ink inside the circle | `Wipe` |
| Rolling text | The clock, the battery, counts, the page and the line roll only the characters that changed | `DigitIn`, `DigitOut` |
| Lists | Rows slide in, a section's rows rise, a heading's letters land, the grid steps back under a sheet; a focused row moves over and brightens; a switch springs; rows leave on close | `RowsIn`, `SectionIn`, `HeadingIn`, `StepBack`, `UnderSheetScale`, `RowFocus`, `Switch`, `SheetOut`, `SheetSwap`, `SettingsOut`, `PanelOut`, `Scroll`, `CoversIn` |
| A folder | Opening: home falls away from it, its games deal in, the name lands, the count rolls, the ring pops in. Closing: the games drop, home pops back in around it | `FolderOut`, `FolderDeal`, `FolderClose`, `FolderPopIn`, `RingPop` |
| The launch | The tile flies up through the hinge, the case bumps, the console's colour takes the Stage through a circle with the title's letters, the Launcher floods to the in-game colour | `Launch` |
| In game | The companion's parts rise in, tabs slide, it rests after 9 s; at the end the parts drop, the colour fades, the ground floods back, the tiles pop in | `InGameIn`, `InGameTab`, `InGameRest`, `InGameOut`, `SessionAchievementBar`, `UnlockLine` |
| The companion's layers (M47 B15, `proposals-screens.html`) | A layer fades in over the companion as its parts drop out, and its name's letters land from below; a move's name lands from the side pressed as the old one leaves the other way (all together in a run). The Shelf's icons grow in from the focused one and move away from it on closing; the focused icon grows, its neighbours step away and it bobs; rows scroll on the soft curve. The Wall's badges grow in by column and row and drop away by column; a filter slides the kept ones to their places and shrinks the rest; a new unlock pulses. The Reel slides a picture's pitch; the picture grows out of its small picture on the Photos tab and goes back into it; a tap fades the type. The hints rise in. Held or quick presses glide on, never restart | `ScreenLettersIn`, `ScreenLettersOut`, `ScreenHintsIn`, `ScreenLayerFade`, `ShelfIn`, `ShelfOut`, `ShelfFocus`, `ShelfScroll`, `ShelfBob`, `WallIn`, `WallOut`, `WallMove`, `WallFilterOut`, `Pulse`, `ReelSlide`, `PhotoOpen`, `PhotoClose`, `ReelChrome` |
| The idle artwork (M47 B15) | The round window rises through the hinge, floats, takes the next game's art through a circle from its bottom while the backdrop changes through its circle behind it; a press drops it back through the hinge | `PortIn`, `PortOut`, `PortFloat`, `PortWipe` |
| Messages | The line under the name rolls to the message and back; an error shakes and holds longer; work still running holds until the app replaces it; a press moves on | `Message` |
| Typing | The keys rise in, the ring lands on the first key, a key pulses when pressed; the typed word is the Stage's name: new letters drop in, a deleted one falls off, the word resizes; Search's hits arrive from above; closing, the keys drop; a folder made is born on its slot | `KeysIn`, `KeyFocus`, `KeyPress`, `TypedLetterIn`, `TypedLetterOut`, `TypedResize`, `TypedNameOut`, `ResultsIn`, `KeysOut`, `FolderBorn` |
| Setup (M47 B14) | A question's rows slide in from the right going on, from the left going back, and its letters come the same way; on the last step the count of games found is typed as the name (its letters drop in), at most one new count every 260 ms, "All set" holds 1.1 s before home | `RowsIn`, `TypedLetterIn`, `SetupCount` |
| Moving | A held tile grows and rocks; each direction trades places with the next slot; A puts it down with a squash, B walks every step back | `HeldScale`, `Held`, `Swap`, `PutDown` |
| The model view (M47 B12, `proposals.html`) | LB or RB on a focused game (in a folder or pinned to home): the name's letters leave and the medium rises through the hinge from its old corner, turning in (two thirds of a turn) on the Flip curve; back again it drops out the way it came and the name lands. At rest it floats with its shadow breathing under it, holds a three-quarter turn and every 15 s turns once all the way round; it rests after a minute alone like the backdrop. Another game is one flip into its medium, the way you moved, from the turn on screen. The stick turns it from the pose it has reached; let go (or R3) it swings back. A wide medium (a jewel case, a cartridge) is set smaller to stay `ModelMaxWidth` wide. The launch's colour grows from the medium in the model view and from the name's middle in the name view | `ModelRight`, `ModelTop`, `ModelScale`, `ModelFloor…`, `ModelMaxWidth`, `ModelIn`, `ModelFlip`, `ModelFloat`, `ModelShowcase`, `ModelSwing`, `HintKey` |

**Still from today's look under Big Type** (not yet given Big Type's numbers; see the B10 note in
`ROADMAP.md`): the edge bump (7% of the ring toward the wall over 300 ms; the mock's is 18 px over
220 ms), the screen swap (X), and the medium's sway (decision 17's spring; the mock's
`MediumSway` is not used), which since M47 B12 is only cover search's case beside the name.

**Holding still.** Handheld mode holds still when any of these asks:
- battery saver, or Android's own reduced-motion setting (the animator scale at 0), read by
  `IDeviceStatusService` (`DeviceStatus.HoldsStill`, M45 H7c);
- under Big Type, the app's own **Reduce motion** switch in Settings → Sound & motion (M47 B10, off by
  default, saved as `AppSettings.HandheldReduceMotion` in the portable `Settings/` folder). It holds
  still whether or not the system asks; either one is enough, and turning it on or off takes effect
  at once, on the Avalonia screens and both native compositors, with no restart. Today's look shows
  no row for it and does not read it.

Held still (`HandheldShellViewModel.HoldsStill`, sent to the compositors as `StageMotionCommand`
and `LauncherMotionCommand`), every move above arrives in place at once, as the mock does under
`prefers-reduced-motion`: the ring and the lift land, nothing breathes, bobs, hops, rocks or rolls,
the new name is there and the old one goes in `LetterOut.stillMs`, the flood lands, the wipe cuts,
lists and keys are simply there, a folder or home is simply there, the takeover is there with no
wait, the edge bump does not nudge, and the backdrop and the medium's sway hold a still frame. The model view's
medium stands at its resting three-quarter turn with no float and no turn, rises, flips and leaves at once, and still
turns under the stick (letting go puts it back at once).

### Today's look (M45; the default until Big Type is made the default)

All animation is **transform and opacity only**, so it can be composited cheaply. There is no
per-frame blur and no animated `BoxShadow`; shadows are pre-rendered images, following the Android
`reduced-effects` lesson. Durations are starting points to tune on the device.

**Rules learned from mockup 2,** where the owner found that animations jumped:

- **Anything that travels uses a real spring, not a timed curve.** That covers the selector and
  scrolling (pages, folder columns).
  - A spring keeps its speed when the target changes, so fast presses glide.
  - A CSS-style transition restarts from zero speed on every press, which reads as a stutter.
  - Mockup values: selector stiffness 560 / damping 43 (barely any overshoot); tracks 250 / 31.5
    (no overshoot).
- **Never animate one property from two places.** The tile's focus lift lives on the tile.
  Fly-in, pop and press animations live on a wrapper around it.
  - When both were on the tile, the lift snapped in the moment a fly-in ended. That was the most
    visible jump.
- **Overshoot is a seasoning.** Small pops (focus lift, press) get about 5–10% overshoot.
  Crossfades and screen-level moves get none.
- **The top screen changes once per move.** Name plate and artwork change together.
  - A single press updates at once.
  - A held direction (presses under 150 ms apart) waits until movement settles.
  - Mockup 2 updated the plate first and the art later, and that read as two jumps.

The motions:

- **Selector.** It must be unmistakable on any folder colour (owner, mockup 3: "I don't understand
  what I selected").
  - A thick violet frame, about 7.5% of the tile wide, with a white keyline inside and out.
  - A soft glow behind it that pulses on a 1.2 s cycle for a few seconds after each move, then rests
    at full strength, so an idle Launcher draws nothing.
  - The focused tile lifts to 1.13× with a deeper shadow.
  - The frame springs from tile to tile.
  - Dock buttons, header chips, drawer apps and sheet rows use the same frame language.
- **Edge bump.** Pressing into a wall nudges the selector about 7% and eases it back, so the
  device clearly says "nothing there".
- **Folder focus.** On the top screen, the big folder grows out of the centre (as for every focus
  change), and after about 200 ms its flap tilts so three covers peek out.
- **Folder open (A): Zoom through** (M45 H4; it replaced H3's spill after the owner judged that
  chaotic in the folder lab). Two objects, one direction, and no tile moves on its own:
  1. The flap swings open on the big top-screen folder at once.
  2. The folder tile grows toward the viewer, 1× → 2.6×, and fades out (300 ms, ease-in-out); its own
     flap swings open as it goes. Home fades out under it (240 ms from 40 ms; no scale).
  3. The whole folder page, header and grid as one surface, grows out of the pressed tile's place,
     0.55× → 1× about the tile's centre (380 ms from 60 ms, on a spring that ends ≈ 2.5% over), fading
     in over its first 45%.
  4. The selector hides at the start and fades back in once the page has landed (from 260 ms, 140 ms),
     already springing to the first game.
  5. At about 300 ms the top screen changes from the open folder to the first game, from the centre.
- **Folder close (B).** The reverse:
  1. The page (pictured as one surface) shrinks back into the tile's place, 1× → 0.55× (260 ms,
     ease-in-out), and is gone by 70% of it, so home shows through. Home fades in (240 ms from 60 ms).
  2. The tile comes back from 2.6× to 1× and fades in (320 ms from 40 ms, on a spring that dips ≈ 1%
     under before it rests), its flap closing.
  3. On top, the big folder grows in from the centre with its flap still open, and closes it 220 ms
     later. The selector hides and returns as on open.
- **Focus change on top (games, folders, apps): from the centre, never from the side** (owner,
  mockup 3, asking for it to feel like the 3DS and Cocoon).
  - The old art gives way at once. (Until the performance pass it was pictured and shrank to 0.86×;
    rendering that picture on every change cost more than it showed.)
  - The new art grows from 0.72× to full size on a real spring with about 2% overshoot (stiffness
    190, damping 17, settling in about 0.45 s). It does not fade in: with the old art gone at once, a
    fade from nothing left the screen blank for a beat on every change.
  - The name plate pops in from 0.94× at the same time.
  - The movement never depends on which direction you pressed.
- **Page change.** The grid slides on the track spring, and the page dots follow.
- **Launch.**
  1. The tile presses in (0.94×) and pops (1.1×).
  2. The top-screen cover zooms in and fades.
  3. Then the emulator starts.
- **Screen swap (X).** Both screens' contents slide out (top goes up, bottom goes down) and the
  other screen's content slides in (about 300 ms). The selector, folder and scroll position are
  all kept.
- **Move mode.** The lifted tile scales up, gains a shadow and wobbles gently. Other tiles slide
  aside to open a gap where it would land. Dropping it gives a small squash-and-settle.
- **Sounds** (Phase 5): soft original effects for move, open, close, swap and launch, and an off
  switch in Settings. They must be self-made or CC0-licensed. No Nintendo sounds. *As built (M45 H6d):* move, open,
  close, confirm and back, synthesised in code; Play is confirm and the swap is silent; the switch is the Start menu's
  Sounds row and the Interface sounds switch in Settings.

**Numbers as built (M45 H3, 2026-11-07; folder open/close and the backdrop revised in M45 H4).** One
table, in code as `HandheldMotion`; the device tuning edits both. They are still the mockup's and the
folder lab's values: the owner's feel check on the Thor is what moves them.

| Motion | Value |
|---|---|
| Selector spring | stiffness 560, damping 43 (ratio ≈ 0.91); position, size and corner radius all ride it |
| Track spring (home pages, folder columns) | stiffness 250, damping 31.5 (ratio ≈ 1.0) |
| Focus lift | 1.13× in 220 ms, cubic-bezier(.34, 1.42, .64, 1), with a deeper pre-rendered shadow fading in |
| Glow breathing | opacity 1 ↔ 0.55, 600 ms each way, 8 half cycles (4.8 s) after each focus change, then rests at 1 |
| Edge bump | 7% of the selector's size toward the wall, out in the first 35% of 300 ms, eased back |
| Top-screen pop | old art gives way at once (no picture); new art 0.72× → 1 on the 190/17 spring (≈ 0.5 s, a few % over), fully opaque from the start; plate 0.94× → 1 in 240 ms, opaque |
| Held direction | presses under 150 ms apart; the top screen updates 140 ms after the last one |
| Folder focus | flap tilts 200 ms after the folder takes the top screen (300 ms transition); the home tile tilts at once |
| Folder open (Zoom through) | tile 1 → 2.6× and fading, 300 ms, cubic-bezier(.4, 0, .2, 1); home fades out over 240 ms from 40 ms; the page (header + grid, one surface) 0.55× → 1 about the tile's centre, 380 ms from 60 ms on a 600/32 spring (≈ 2.5% over), opacity 0 → 1 over its first 45%; selector hidden until 260 ms, then fades in over 140 ms; the top screen holds the open folder 300 ms |
| Folder close | the page's picture 1 → 0.55× about the tile's centre, 260 ms, opacity gone by 70%; home fades in over 240 ms from 60 ms; tile 2.6× → 1 on a 576/38.8 spring (≈ 1% under) and fading in, 320 ms from 40 ms; selector as on open; the Stage's folder arrives open and closes its flap after 220 ms |
| Stage backdrop (era motif) | a change waits for 250 ms without another, then crossfades over 450 ms (one leaving layer at most); motion stepped in whole pixels, 4×/s for the blocks and 6×/s for the bar, stopped while hidden or in the background. Cartridge: tint hsl(h 40% 92%) → hsl(h 38% 86%); grid 22 px pitch, 2 px lines, hsl(h 50% 60% / .10); blocks 22 px, 8 per 176×132 tile, hsl(h−20/h/h+20 55% 62% / .22), drifting (176, 132) px per 22 s; pool hsl(h 35% 97% / .8) → clear at 75% of a 55% × 70% ellipse. Disc: radial hsl(h 35% 90%) → hsl(h 30% 78%) over 90% × 120% from (50%, 40%); scanlines 2 px of 5, hsl(h 40% 30% / .09); bar 140 px, hsl(h 60% 96% / .35) at its middle, top to bottom (1220 px) every 24 s; vignette clear to 55% then hsl(h 40% 40% / .18) |
| Idle drift (M45 H6d) | starts 15 s after the last press or touch; each recent game as its hero, held 6 s, 1.2 s crossfade (the outgoing hero stays under the arriving one); once round and back to the newest, then rests; not navigation, so the Stage's own rest after a minute still applies |
| Launch | Launcher cover presses to 0.94×, pops to 1.1× (260 ms) and lifts off (420 ms); the Stage's cover lands from below after 140 ms (520 ms) and zooms to 1.7× fading (460 ms); the emulator starts as it lands (660 ms after Play) |


## What already exists, and what is new

This comes from a read of the code on 2026-11-06 covering Gamepad mode (`MainViewModel`, the
`Gamepad*` view models, `GamepadShellView`) and the Android companion (`SecondScreenViewModel`,
`SecondScreenView`, `SecondScreenController`).

**The short answer:** almost everything a user *does* in Handheld mode already exists as logic.
The new work is mostly views and motion. There are four real pieces of new logic:

1. Arranging the home screen.
2. The plumbing for a third mode on Android.
3. One controller selector that spans both screens and the companion.
4. A handful of small additions.

### Kept 1:1 (existing logic; only a new view in the comfy look)

| Area | What it is today | Backed by |
|---|---|---|
| Launching | Picking the screen (per-system Ask / Built-in / External; DS/3DS always built-in), file-missing checks, save sync before and after, the "Launching…" and blocking sync panel, play-time recording, achievements refresh on return | `MainViewModel.LaunchGameCoreAsync`, `AndroidEmulatorLaunchService`, `LaunchScreenResolver` |
| Launch-screen chooser | Two screen cards plus "Always use this screen" | `GamepadOverlayKind` chooser |
| Disc chooser | Choose and remember the active disc | `MainViewModel` disc selection |
| Game actions | Launch, Select disc, Achievements, Edit title, Set cover, Scrape, Remove from library (with confirmation) | `MainViewModel` game-actions overlay |
| Achievements (full screen) | RetroAchievements and Steam; All / Locked / Unlocked, sort, refresh, reveal | `AchievementDetailsViewModel` |
| Cover search | DuckDuckGo image search, preview and apply | `GamepadCoverSearchViewModel`, `CoverSearchViewModel` |
| Scraper | Single game, with all its states (consent, no match, applying…) | `GamepadScraperViewModel`, `GameScraperViewModel` |
| Batch scraper | Configure, run, done | `GamepadBatchScraperViewModel` |
| Search | Filters by title | `LibraryViewProjection` (on X today; in the Start menu here) |
| Add games | Android folder picker, then the system chooser (under Big Type, setup's first question and its "which system", M47 B14) | `MainViewModel` import flow |
| Settings: Library | Rescan all, change data folder, accurate play time, run setup again | `GamepadSettingsViewModel` |
| Settings: Emulators | Close emulator on return (Shizuku), per-platform emulator choice, launch screen, game folders | `GamepadSettingsViewModel`, `EmulatorSettingsViewModel` |
| Settings: Achievements | Steam profile and key, RetroAchievements account, refresh matches | same |
| Settings: Artwork & metadata | Fetch after import, fetch missing, web image search, ScreenScraper sign-in | same |
| Settings: Saves | Google Drive, cloud folder, per-platform save and state folders, replace cloud/local, export | same |
| Settings: Sound (Classic) or Sound & motion (Big Type) | Interface sounds, plus Reduce motion in Big Type | same |
| Settings: About | Version, check for and install updates (`AndroidUpdateApplier`) | same |
| On-screen keyboard | The app's own couch keyboard, mirrored onto the Thor's second screen | `AndroidOnScreenKeyboardService` |
| Setup wizard | Storage access, data folder, second screen, closing games, games and emulators, saves | `SetupWizardViewModel`, `StartupViewModel` |
| Status | Toasts (info, progress, error), empty library, loading | `MainViewModel` |
| Play data | Last played, play time, play count; used by sorts and the name plate | `Game.LastPlayedAt`, `Playtime`, `PlayCount` |
| Companion: resting and wake | Dims during play; a touch wakes it for 5 s | `SecondScreenViewModel` standby |
| Companion: achievements | Badge grid, detail, refresh, follows the running or focused game | `SecondScreenViewModel`, `SecondScreenController` |
| Companion: photos and video | Screenshots, then a muted video that downloads only on play | `SecondScreenViewModel.Media`, `ICompanionMediaSource` |
| Dock and app drawer | Five slots, pin (moves the app if it's already pinned) and clear, launch on Screen-2, hide and come back via the accessibility watcher | `SecondScreenDock`, `FileSecondScreenDockStore`, `SecondScreenController` |
| Game-running lifetime | Keep-alive service, return detection, "game took Screen-2" handling | `SecondScreenController`, `SecondScreenKeepAliveService` |

Already gone on Android, so not needed here: Hotkeys, Texture Packs, Sync RPCS3, the local cover
file picker and Desktop mode.

### New logic

| What | Why it's new | Size |
|---|---|---|
| **Home layout and user folders** | Favorites, collections, custom order and pinning games to home don't exist anywhere in the code today. This needs the `HandheldHomeLayout` model, a portable store (`Settings/handheld-home.json`), reconciliation when platforms or games appear and disappear, user folders (name, colour, games), and a manual order within folders. | Medium. It lives in Core and is fully unit-testable on macOS. |
| **Third interface mode on Android** | `AndroidInterfaceModeService` fixes Android to Gamepad mode today. Needed: `InterfaceMode.Handheld`, switching between modes, the main screen hosting the Stage, and the Presentation hosting the Launcher while browsing and the Companion while a game runs. | Medium. The risky part is device behaviour, which Phase 0 tests. |
| **Screen swap** | Re-host the Stage and Launcher between the two displays. There is a precedent: the companion already moves to the main screen via `MainScreenCompanion`. | Small to medium |
| **One selector across both screens** | Both windows send controller input to one dispatcher, which is the same pattern as today. New: the companion's dock, drawer and overlay buttons work with the D-pad (today they are touch-only). | Small to medium |
| **Move mode** | Lift, carry and drop with the pad and touch. Only visual on top of the layout model. | Small |
| **Sort chip inside folders** | Manual is new. A–Z, Last played and Most played come from existing data. | Small |
| **Companion achievement tabs** | All / Locked / Unlocked is logic Gamepad mode already has; it gets wired into the companion. | Small |
| **Sounds** | Short original effects and an off switch. | Small |
| **Dock app icons at top-screen size** | Load launcher icons at about 512 px instead of 96 px, and pick each app's colour from its icon once. | Small |

### Purely visual (the bulk of the remaining work)

- The Stage, Launcher and Companion views.
- The folder graphic in the chosen style.
- The selector, all the motion in the motion spec, and the restyled sheets and overlays.
- Every settings page in the comfy look, built on today's row kinds (toggle, choice, action, text,
  secret, folder, file, information, summary).

## Architecture

- **New mode value.** Add `InterfaceMode.Handheld` in Core. It is offered on Android only when a
  presentation display is present. Every mode's Start/Settings menu can switch to it and away
  from it.
- **Both screens are Avalonia.**
  - The second screen already hosts an `AvaloniaView` inside `ThorSecondScreenPresentation`.
  - In Handheld mode, that Presentation shows the Launcher, and `MainActivity`'s single view shows
    the Stage.
  - **On Android the Stage is drawn natively** (M45 H4, option 5): a `GLSurfaceView` over the
    activity's Avalonia surface, with its own GL thread running `StageCompositor`
    (`EmuShelf.Rendering/Stage`). `HandheldStageView` stays laid out but invisible and only lends
    pictures of its pieces (centre art, plate, corner pills, the backdrop's layers), taken once per
    change by `HandheldNativeStageFeed`; the compositor does all the motion (the pops, the backdrop
    crossfade and drift) and renders the 3D medium into its own texture, so a pop only scales it. The
    Title page's hero (fan art and logo, or the cover on its tint) is native too: pictured as layers and
    crossfaded in and out (340 ms), and the launch flight lands natively. Everything falls back to the
    Avalonia Stage if GL ES 3 cannot start. A folder's art is
    sent as parts (shadow, back plate, the three covers, the flap) with the transform each state gives
    them, read from the XAML's own `peek`/`open` styles, and glides between states natively on the
    same curve as the XAML transitions.
  - **On Android the Launcher is drawn natively too** (same design): `LauncherCompositor` on its own
    GL thread, in a `GLSurfaceView` the Presentation lays over its `AvaloniaView`. Every tile, chip and
    dock button is pictured once per change (padded `shot` wrappers keep shadows in the picture) by
    `HandheldNativeLauncherFeed`; the native side does the tracks, the focus lift and its deeper shadow,
    the selector (spring in position, size and radius; its bands drawn by a shader; the glow pictured per
    size and breathing), the edge bump and the Zoom through. The Title page is native too (its body,
    buttons, the disc/actions sheet, the screen chooser, the launching panel with a native indeterminate
    bar, and the cover's lift-off). The menu and the All apps drawer still draw on the Avalonia Launcher,
    which takes the screen back while one is open. Home folders glide their flap natively too (peek on
    focus, open with the zoom).
- **One view model, two views.**
  - `HandheldShellViewModel` owns all state: home layout, open folder, selection, move mode, swap
    state, and the overlays.
  - `HandheldStageView` and `HandheldLauncherView` are pure views onto it.
- **Swap by re-hosting, not by moving windows.**
  - Each display has a root slot that shows either the Stage or the Launcher, decided by
    `IsSwapped`.
  - There is already a precedent: `SecondScreenController` re-hosts the companion model on the
    main screen via `MainScreenCompanion` when a game takes Screen-2.
- **Input.**
  - Android sends pad events to whichever display was last touched. So both
    `MainActivity.DispatchKeyEvent` and the Presentation's `DispatchKeyEvent` forward to the same
    `HandheldShellViewModel.DispatchGamepadAction`.
  - There is one selector, whichever screen has focus. This is the same pattern as the
    2026-08-23 second-screen gamepad decision.
- **Home layout model.**
  - `HandheldHomeLayout` holds pages of slots. Each slot holds a tile reference: a platform folder
    (by system id), a user folder (by folder id), a game (by game id), or a system tile (by kind).
  - User folders store their name, colour, ordered game ids and sort mode.
  - It persists to portable `Settings/handheld-home.json`. The model and store live in Core, so
    the macOS desktop test suite covers them; this is the same pattern as
    `second-screen-dock.json`.
- **Reconciliation.** New platforms get a folder appended at the end. Ids for removed games or
  platforms are dropped quietly. Missing files show the usual "unavailable" dimming.
- **Reuse rather than duplicate:**
  - `GameViewModel` and the cover/thumbnail pipeline, with tiles decoded at tile size off the UI
    thread.
  - The Android launch path and the achievements services.
  - Gamepad mode's Settings, scraper and cover-search overlays, shown on the top screen.
- **During gameplay.** Keep the existing companion lifetime rules: the game idle screen,
  keep-alive, and a game or app taking Screen-2. Handheld mode changes browsing, not how sessions
  are handled.

## Phases

**The build plan is `ROADMAP.md` → M45**, which turns these phases into sub-milestones H0–H6 with
exit criteria and the code each one touches; run one per session with `/milestone M45 H<n>`. The
phases below are kept as the original sketch.

Each phase must be usable and checked on the Thor before the next one starts. Screen-2 is
`FLAG_SECURE`, so checking it on the device means hands on the device plus logs, not screenshots.

### Phase 0 — device spike

- Get both Avalonia surfaces animating at the same time on the Thor.
- Glide a selector over 20 cover tiles on the bottom while the top cross-fades.
- Re-host the two views between displays.
- Route the pad from either display to one view model.

**Exit:** frame times are recorded, and both screens animate smoothly enough to commit to Avalonia
for both. If they don't, revisit before Phase 1.

### Phase 1 — mode and skeleton

- `InterfaceMode.Handheld` and switching into and out of it.
- Stage and Launcher views.
- Platform folders created automatically, in the default layout.
- Open a folder, scroll the game grid, launch a game, B back.
- Plain transitions only.

**Exit:** a game can be launched from Handheld mode, and you can get back to Desktop/Gamepad mode.

### Phase 1b — Title page (round 5)

- A on a game opens the Title page; Play launches. Disc and screen choosers as states of it.
- Fan art and logo on the top screen for the Title page, with the cover as fallback.
- The bottom-to-top launch motion.

**Exit:** every launch goes through the Title page, including the existing choosers and the
save-sync panel.

### Phase 2 — motion pass

- Selector spring, focus lift and breathing.
- Edge bump.
- Folder focus, open and close.
- Top-screen game change with a debounce while the d-pad is held.
- Page slides.
- Launch animation.

**Exit:** the owner judges it on the Thor; this phase is judged by feel, not just by checklist.

### Phase 3 — screen swap

- X swaps the Stage and Launcher between displays with the slide animation.
- The selector, folder and scroll position survive the swap.
- Whether the screens are swapped is remembered.

### Phase 4 — arranging the home screen

- Layout model and store, plus reconciliation. Visible empty slots; the Recent folder.
- Move mode with the pad and with touch, including dropping a game on a game (new folder) or on
  one of your folders (add).
- User folders: create from an empty slot, rename and recolour on the keyboard, delete with the
  games kept. The empty-folder state.
- Add to folder, pin to home.
- Sort chip inside folders.
- The idle drift.

**Exit:** the layout survives a cold start and a reinstall-style restore.

### Phase 5 — actions, system and sound

- Y actions sheet.
- Start system menu (Settings, Achievements, Search, switch mode), reusing the couch overlays.
- Sound effects with an off switch.

### Later (not part of this work)

- Square icon art, hero/fanart and logos for the Stage (the ScreenScraper work).
- Themes and widgets.
- A status pill.
- Grid size options.
- A single-screen fallback.
- Other dual-screen devices (AYANEO Pocket DS, Retroid dual-screen).

## Open questions

1. **Why it still feels unfinished.** The owner's latest verdict. The leading suspects are listed in
   [docs/prototypes/handheld-mode/README.md](docs/prototypes/handheld-mode/README.md) → "What is
   still unfinished": a thin top screen, placeholder art, a generic visual identity, motion never
   tried on the device, and most screens not drawn yet. Next step: an independent review.
2. **Dual-screen games (DS/3DS).** The rule is that the companion should not show for them. The
   code today always launches DS/3DS on the built-in screen and leaves the companion on Screen-2
   in its resting dim. Nothing detects an emulator that draws its own second screen, and the
   companion sits above ordinary app windows. Check on the Thor what melonDS/Azahar actually do,
   then decide whether the companion should step aside for `IsDualScreen` systems.
3. **Themes.** Gamepad mode has a Themes page. Hide it in Handheld mode for now (decision 6), or
   keep it?
4. **The mode's name.** Handheld, Pocket, Home, or something else.
