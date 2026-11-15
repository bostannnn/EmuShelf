// Big Type: the frozen numbers.
//
// Every value is at the Thor's resolution: the Stage is 1920x1080, the Launcher 1240x1080. The mock in
// index.html draws at half of these. Sizes and positions are pixels, times are milliseconds, opacities and
// scales are plain numbers.
//
// This file is the reference. The mock reads the console colours and the legibility rule from it and prints
// the whole of it under "Frozen numbers"; the app's Handheld resource dictionary mirrors it name for name
// (M47 B1 makes the names, B2 to B9 take the values). Change a number here first.
window.BT_TOKENS = {
  version: '2026-11-13',

  face: {
    Family: 'M PLUS Rounded 1c',
    WeightText: 500,
    WeightStrong: 700,
    WeightDisplay: 900,
    DisplayLetterSpacing: -0.02,   // em
  },

  // Type. Size in px unless it says Fit.
  type: {
    DisplayName:        { fitBox: [1360, 680], fitBoxBesideMedium: [1120, 680], weight: 900, lineHeight: 0.9, maxLines: 3 },
    Heading:            { size: 100, weight: 900 },                 // folder name, Settings, a panel's title
    HeadingCount:       { size: 100, weight: 900, opacity: 0.45 },  // the count beside a folder name
    PanelCount:         { size: 68,  weight: 900, opacity: 0.45 },
    SheetTitle:         { size: 80,  weight: 900 },                 // the Y menu, the keyboard's heading
    Clock:              { size: 92,  weight: 900 },
    Label:              { size: 44,  weight: 700 },                 // the console name, top left
    Line:               { size: 42,  weight: 700 },                 // the line under the name; messages use it
    Facts:              { size: 36,  weight: 700, opacity: 0.82 },  // the second line: emulator, last played, disc
    Battery:            { size: 36,  weight: 700, opacity: 0.85 },
    Row:                { size: 40,  weight: 700 },                 // menus, Settings, panels
    RowValue:           { size: 32,  weight: 700, opacity: 0.85 },
    RailRow:            { size: 46,  weight: 700 },                 // Settings sections
    Hint:               { size: 38,  weight: 700 },
    HintGlyph:          { size: 58,  stroke: 5, letter: 28 },       // the outlined circle with A, B, X, Y
    Key:                { size: 42,  weight: 700 },
    DockLetter:         { size: 32,  weight: 800 },
    Monogram:           { fitWidth: 0.76, maxSize: 0.56, weight: 900 },  // of the tile's side
    SessionTimer:       { size: 380, weight: 900 },
    InGameFigure:       { size: 224, weight: 900 },
    InGameStat:         { size: 64,  weight: 900 },
    InGameLabel:        { size: 36,  weight: 700, opacity: 0.75 },
    LaunchTitle:        { sizes: [224, 160, 128], weight: 900 },    // up to 12 letters, up to 22, longer
    // B8, the mock's launch and in-game companion (its inline sizes, doubled):
    LaunchStatus:       { size: 44,  weight: 700, opacity: 0.9 },   // the line under the launch title: syncing, what is running
    InGameTitle:        { size: 80,  weight: 900 },                 // the game's title under "Now playing", set smaller to fit
    InGameTab:          { size: 34,  weight: 700, restOpacity: 0.45 },  // Session, Achievements, Photos; the one showing at 1
    InGameStatLabel:    { size: 32,  weight: 700, opacity: 0.75 },  // "in total", "achievements" beside their figures
    InGameRecent:       { size: 38,  weight: 700 },                 // an achievement unlocked lately
    InGameRecentWhen:   { size: 32,  weight: 700, opacity: 0.7 },   // when it was unlocked
    // B9, the mock's keyboard (its inline sizes, doubled): the note under the keyboard's heading ("It lands on the slot you picked.").
    KeyboardNote:       { size: 36,  weight: 700, lineHeight: 1.3, opacity: 0.75 },
  },

  // The Stage, 1920x1080. Text sits in the four corners.
  stage: {
    LabelLeft: 88,  LabelTop: 64,
    ClockRight: 88, ClockTop: 48,
    NameLeft: 80,   NameBottom: 240,
    LineLeft: 88,   LineBottom: 132,
    FactsLeft: 88,  FactsBottom: 76,
    HintsRight: 88, HintsBottom: 72, HintGap: 44,
    MediumRight: 248, MediumTop: 236, MediumWidth: 392, MediumHeight: 552,
    NameDimmedOpacity: 0.5,          // a game whose file is missing
    // The launch (B8, the mock's .launch): the title's lines centred, at most LaunchTitleMaxWidth wide, LaunchTitleLineHeight of its
    // size apart, the status LaunchGap under it; the block in the middle of the Stage.
    LaunchTitleMaxWidth: 1600, LaunchTitleLineHeight: 0.92, LaunchGap: 36,
  },

  // The Launcher, 1240x1080.
  launcher: {
    HomeColumns: 5, HomeRows: 4, HomeLeft: 108, HomeTop: 44, HomePitch: 212,
    FolderRows: 3, FolderColumnsVisible: 4, FolderLeft: 162, FolderTop: 208, FolderPitch: 236,
    HeaderLeft: 162, HeaderTop: 40, HeaderHeight: 136,
    DockBottom: 32, DockGap: 24,
    RowPitch: 88, RowPitchPanel: 92, RowPitchSettings: 104, PanelRowsVisible: 7,
    // The lists (the mock's openSheet, openSettings and openPanel, B7). A row is its pitch less RowGap high, its value RowValueGap
    // from its label; the focused one moves RowFocusShift right and the ring sits RowRingOutsetX beside it and RowRingOutsetY above
    // and below. The Y sheet and the menu: their title at SheetLeft / SheetTitleTop, at most SheetWidth wide, the rows from
    // SheetTop, SheetRowsVisible at a time. Settings: its heading at SettingsLeft / SettingsHeadTop, the sections' rail from
    // SettingsLeft and the rows from SettingsRowsLeft, both from SettingsTop, SettingsRowsVisible at a time (the rest scroll). A panel: its heading and count in a box PanelHeadTop
    // from the top, PanelHeadHeight high and PanelLeft in from each side (the name at most PanelNameMaxWidth wide, the count raised
    // PanelCountRaise), the rows from PanelRowsTop in a window PanelRowsVisible rows high, inset PanelRowsInset, PanelWidth wide.
    RowGap: 16, RowValueGap: 28, RowFocusShift: 10, RowRingOutsetX: 28, RowRingOutsetY: 12,
    RowGroupIndent: 48,   // a Settings row inside an opened one (an emulator's options): the app's own indent; the mock has none
    // Not the mock's (its labels all fit): a rail name or row label too long for its room is set smaller to fit, as the mock's setHead
    // fits a heading, but no smaller than LabelFitMin of its size; past that it ends in an ellipsis. A row's value takes at most
    // RowValueMaxShare of the row, so a long one (a folder's path) gives way to the label and never touches it.
    LabelFitMin: 0.75, RowValueMaxShare: 0.45,
    SheetLeft: 140, SheetTitleTop: 60, SheetTop: 208, SheetWidth: 960, SheetRowsVisible: 8,
    SettingsLeft: 80, SettingsHeadTop: 44, SettingsHeadMaxWidth: 1080, SettingsTop: 224, RailWidth: 300,
    SettingsRowsLeft: 472, SettingsRowsWidth: 688, SettingsRowsVisible: 7,
    PanelLeft: 80, PanelHeadTop: 40, PanelHeadHeight: 136, PanelNameMaxWidth: 680, PanelCountRaise: 12,
    PanelRowsTop: 200, PanelRowsInset: 16, PanelWidth: 1080,
    CoverRowsVisible: 2,   // cover search's candidates: two rows of the folder's tiles in view (the mock's eight); more scroll
    KeyPitch: 112, KeyboardLeft: 66, KeyboardTop: 424,
    // The keyboard (B9, the mock's openKb, buildKeys and mkKey). Its three rows of letters sit KeyPitch apart from KeyboardTop, each
    // row centred on the widest (KeyColumns keys from KeyboardLeft), which puts the mock's rows at its own indents (0, 56 and 168);
    // under them Space, Delete and Done, KeySpaceWidth and KeyActionWidth wide. The ring sits KeyRingOutset round a key. Its heading
    // (SheetTitle) at KeyboardHeadLeft / KeyboardHeadTop, at most KeyboardHeadMaxWidth wide, the note (KeyboardNote) at
    // KeyboardNoteLeft / KeyboardNoteTop. Search's results: up to ResultsMax of home's tiles in a row from ResultsLeft / ResultsTop,
    // ResultsPitch apart.
    KeyColumns: 10, KeySpaceWidth: 548, KeyActionWidth: 268, KeyRingOutset: 14,
    KeyboardHeadLeft: 66, KeyboardHeadTop: 44, KeyboardHeadMaxWidth: 1120, KeyboardNoteLeft: 70, KeyboardNoteTop: 224,
    ResultsLeft: 66, ResultsTop: 200, ResultsPitch: 200, ResultsMax: 5,
  },

  // The in-game companion (B8, the mock's launch builds it), on the Launcher's 1240x1080 in the in-game colour. Tops are the top of each
  // text's line box (1.15 of its size), as the mock places them.
  ingame: {
    KickerLeft: 80, KickerTop: 60,                      // "Now playing", in InGameLabel
    TitleLeft: 80, TitleTop: 108, TitleMaxWidth: 1080,  // the game's title, in InGameTitle
    TabsRight: 80, TabsTop: 60, TabsGap: 36,            // Session, Achievements, Photos
    TimerLeft: 68, TimerTop: 244,                       // the session's time, in SessionTimer
    LabelLeft: 88, LabelTop: 660,                       // "this session", in InGameLabel
    StatsInset: 80, StatsTop: 736, StatGap: 20,         // the total and the achievements, at either end, each figure beside its label
    FigureLeft: 72, FigureTop: 264, FigureLabelTop: 544,   // the Achievements tab's figure, in InGameFigure, and "unlocked"
    RecentInset: 80, RecentTop: 676, RecentPitch: 60,   // the three latest unlocks
    ShotLeft: 80, ShotTop: 256, ShotPitchX: 376, ShotPitchY: 232, ShotWidth: 328, ShotHeight: 200, ShotRadius: 20, ShotsLabelTop: 744,
    BarInset: 80, BarTop: 868, BarRadius: 6,            // ProgressHeight high, white over white at BarTrackOpacity
    BarTrackOpacity: 0.2,
  },

  shape: {
    TileHome: 176, TileFolder: 208, TileRadius: 30,
    DockTile: 88, DockRadius: 22,
    Key: 100, KeyRadius: 24,
    RingStroke: 8, RingOutset: 26,          // from the tile's resting edge to the ring's outer edge
    RingRadiusTile: 50, RingRadiusRow: 32, RingRadiusDock: 38, RingRadiusKey: 34,
    FocusScale: 1.10, DockFocusScale: 1.14, KeyFocusScale: 1.08, HeldScale: 1.20,
    NeighbourPush: 8,
    SwitchWidth: 84, SwitchHeight: 48, SwitchKnob: 26,
    SwitchStroke: 5, SwitchKnobInset: 6, SwitchKnobTravel: 36, SwitchOnKnobScale: 1.15,   // on: filled white, the knob in the ground's colour
    UnderSheetScale: 0.95,                  // the grid steps back under a sheet or the menu
    ProgressHeight: 12,
  },

  colour: {
    OnGround: '#FFFFFF',                   // type and glyphs on the Launcher
    TileRest: 'rgba(255,255,255,0.17)',    // a folder tile, a dock tile, a key
    TileFocus: '#FFFFFF',                  // the focused one; its ink is the console's colour
    Ring: '#FFFFFF',
    Danger: '#FFC9BD',
    PlaceholderOpacity: 0.34,              // B9: the typed word's placeholder ("Name", "Search") on the Stage
    RowRestOpacity: 0.58, RowLockedOpacity: 0.34, RowLockedFocusOpacity: 0.8, SwitchOffOpacity: 0.65, UnderSheetOpacity: 0.12, EmptySlotDotOpacity: 0.30, MissingTileOpacity: 0.45,
    InkToward: '#0B0A18', InkMix: 0.74,    // the Stage's dark ink: the console colour mixed this far toward InkToward
    InGameMix: 0.64,                       // the Launcher while a game runs
    InGameRestOpacity: 0.28,
    // One flat colour per console, tuned by hand and approved by the owner. White type holds 4.5 to 1 on each
    // and 3 to 1 on a tile; no two are closer than 19 (CIE76).
    consoles: {
      nes: '#7C5F50', snes: '#91408C', gbc: '#2A7D52', gba: '#7648A8',
      gamecube: '#5548C8', nds: '#6272B8', wii: '#198098', '3ds': '#C0446E',
      megadrive: '#175A4F', dreamcast: '#B8521E',
      playstation: '#6B6F7E', playstation2: '#2B5BC7', psp: '#3D3D48', playstation3: '#262E7A',
      arcade: '#A8372E', steam: '#30587F',
    },
    Recent: '#39465F', Settings: '#4A43B8',
    userFolders: ['#A8503C', '#3F6FB8', '#96702A', '#2A8766', '#8A4A8F'],
  },

  motion: {
    Land:  'cubic-bezier(.2, 1.5, .35, 1)',     // anything arriving
    Leave: 'cubic-bezier(.5, 0, .9, .5)',       // anything going; always quicker than landing
    // The name's letters (setWord's ENTER and EXIT): along a move left or right fromEm, up or down fromEmVertical; into a
    // folder they rise from the hinge (openEm, tilted openTilt degrees), out of one they drop in (backEm), and with no
    // move they rise (riseEm). Leaving goes the other way. Under reduced motion the new name is simply there and the old
    // one goes in stillMs.
    LetterIn:  { ms: 540, delay: 90, staggerMax: 26, staggerTotal: 300, fromEm: 0.55, fromEmVertical: 0.5,
                 openEm: 0.95, openTilt: 7, backEm: -0.7, backTilt: -5, riseEm: 0.7, riseTilt: 5 },
    LetterOut: { ms: 170, staggerMax: 12, staggerTotal: 110, toEm: 0.45, openEm: -0.6, backEm: 0.6, riseEm: -0.4, stillMs: 60 },
    // The word being typed is the Stage's name and stays one layer (B9, the mock's typeWord): only new letters drop in (TypedLetterIn,
    // from fromEm above, tilted tilt, stagger ms apart, on the Land curve); a deleted letter falls off the end (TypedLetterOut: dropEm
    // down, turning tilt, on the Leave curve); the word is set smaller or larger as it grows, eased over TypedResize (CSS ease-out); the
    // name that was there before leaves upward first (TypedNameOut, stagger ms a letter up to staggerTotal, on the Leave curve). The
    // placeholder ("Name", "Search") shows at colour.PlaceholderOpacity.
    TypedLetterIn: { ms: 460, fromEm: 0.55, tilt: -8, stagger: 30 },
    TypedLetterOut: { ms: 260, dropEm: 0.6, tilt: 14 },
    TypedResize: { ms: 200 },
    TypedNameOut: { ms: 170, stagger: 10, staggerTotal: 110, toEm: -0.6 },
    // The keyboard (B9, the mock's openKb and closeKb). Its keys rise in (KeysIn: from rise px below, on the Land curve, rowStagger ms a
    // row and keyStagger ms a key along it); ringAfterMs after it opens the ring is on the first key. A key that takes the focus turns
    // white and grows (KeyFocus: its colours over fadeMs, its size over ms on the Land curve); a key pressed or tapped pulses from
    // KeyPress.fromScale. Search's results arrive from above (ResultsIn: from `from` px, at fromScale, stagger ms apart, on the Land
    // curve). Closing, the keys and results drop (KeysOut: drop px over ms on the Leave curve, stagger ms apart counted every
    // staggerEvery keys) and after holdMs the layer under comes back; a folder made is born on its slot (FolderBorn: from fromScale,
    // turned fromTilt degrees, on the Land curve).
    KeysIn:    { ms: 440, rise: 140, rowStagger: 50, keyStagger: 12, ringAfterMs: 260 },
    KeyFocus:  { ms: 300, fadeMs: 200 },
    KeyPress:  { ms: 300, fromScale: 0.95 },
    ResultsIn: { ms: 420, stagger: 45, from: -60, fromScale: 0.6 },
    KeysOut:   { ms: 170, drop: 100, stagger: 8, staggerEvery: 10, holdMs: 290 },
    FolderBorn: { ms: 600, fromScale: 0, fromTilt: -25 },
    // Pick up and put down (B9, the mock's startMove, moveKey and endMove). Held, the tile grows to shape.HeldScale above its
    // neighbours and its inside rocks between -Held.degrees and +Held.degrees, wiggleMs each way (CSS ease-in-out); each trade of places
    // slides both tiles to their new slots (Swap, on its curve). Put down, its inside squashes PutDown.from, then low, then 1 over ms
    // (CSS ease-out).
    Held:      { wiggleMs: 420, degrees: 3.5 },
    Swap:      { ms: 360, easing: 'cubic-bezier(.2, 1.3, .35, 1)' },
    PutDown:   { ms: 380, from: 1.12, low: 0.92 },
    DigitIn:   { ms: 400, delay: 50, stagger: 12, travelEm: 1.1 },
    DigitOut:  { ms: 220, stagger: 12, travelEm: 1.1 },
    Ring:      { ms: 300, coversBothAt: 0.38 },
    FocusLift: { ms: 380 },
    Flood:     { ms: 640, easing: 'cubic-bezier(.2, .6, .2, 1)', radius: 1640 },
    Wipe:      { ms: 760, delay: 110, easing: 'cubic-bezier(.2, .6, .2, 1)', radius: 2300, below: 40 },   // from the focused tile's x, below the Stage's bottom edge
    Scroll:    { ms: 500, easing: 'cubic-bezier(.2, 1.15, .3, 1)' },
    // Opening and closing a folder (B8, the mock's openFolder, fallAway, closeFolder and popIn). Opening: home's tiles fall away from the
    // opened one on the Leave curve (FolderOut: each goes `push` px for every cell it stands from it, shrinking to toScale; the opened one
    // grows to openedScale after openedDelay; staggerPerTile ms per cell of distance, at most staggerSteps), then after holdMs the
    // folder's games deal in on the Land curve (FolderDeal: rising `rise` px from fromScale, by column + row, at most staggerSteps), and
    // ringAfterMs later the ring pops in on the first game (RingPop, from folderFromScale). Closing: the games drop out on the Leave curve
    // (FolderClose: `drop` px to toScale, staggerPerTile ms for every step of column + row short of staggerFrom), after holdMs home's
    // tiles pop back in around the folder's tile (FolderPopIn: from fromScale on the Land curve, staggerPerTile ms per cell of distance,
    // at most staggerSteps) and ringAfterMs later the ring is on it again. Under reduced motion the folder or home is simply there.
    FolderOut: { ms: 250, staggerPerTile: 20, push: 56, toScale: 0.55, openedScale: 1.7, openedDelay: 70, staggerSteps: 6, holdMs: 370 },
    FolderDeal: { ms: 480, staggerPerTile: 45, rise: 160, fromScale: 0.55, staggerSteps: 8, ringAfterMs: 230 },
    FolderClose: { ms: 200, staggerPerTile: 16, drop: 120, toScale: 0.6, staggerFrom: 5, holdMs: 300, ringAfterMs: 130 },
    FolderPopIn: { ms: 440, staggerPerTile: 30, fromScale: 0.45, staggerSteps: 6 },
    // The ring popping in on the Land curve: on another home page (B5) from fromScale, on a folder's first game from folderFromScale.
    RingPop:   { ms: 320, fromScale: 1.25, folderFromScale: 1.3 },
    // Rows arrive from the right (the sections' rail from the left, railSlide), the first after delay. A Settings section's rows,
    // when the section changes, rise from the way you moved (SectionIn). A heading's letters (the mock's lettersIn) rise fromEm
    // (sheetFromEm on a sheet's or the menu's title) tilted tilt degrees, one every stagger ms up to staggerMax. Under a sheet or the
    // menu the grid steps back (StepBack: its scale on the curve, its opacity in fadeMs); a row taking the focus moves over and
    // brightens (RowFocus), a switch's knob springs across (Switch). Under reduced motion all of these are simply there.
    RowsIn:    { ms: 420, stagger: 32, slide: 140, delay: 40, railSlide: -100 },
    SectionIn: { ms: 400, stagger: 30, rise: 52 },
    CoversIn:  { ms: 460, staggerPerTile: 45, rise: 120, fromScale: 0.6 },   // cover search's candidates, by column + row
    HeadingIn: { ms: 520, delay: 60, stagger: 24, staggerMax: 360, fromEm: 0.9, sheetFromEm: 0.8, tilt: 6 },
    StepBack:  { ms: 400, fadeMs: 280, easing: 'cubic-bezier(.2, 1.2, .3, 1)' },
    // Rows leaving on the Leave curve (the mock's closeSheet, openSheet over another, closeSettings, closePanel): each row goes `to`
    // px (right, left, or down for drop) and clears over ms, stagger ms apart (a panel's at most staggerCap rows apart); the list
    // goes after holdMs.
    SheetOut:    { ms: 160, stagger: 12, to: 120, holdMs: 240 },
    SheetSwap:   { ms: 150, stagger: 10, to: -120, holdMs: 210 },
    SettingsOut: { ms: 180, stagger: 10, drop: 80, holdMs: 290 },
    PanelOut:    { ms: 170, stagger: 10, staggerCap: 8, drop: 80, holdMs: 280 },
    RowFocus:  { ms: 350, fadeMs: 200 },
    Switch:    { ms: 350, fadeMs: 250 },
    // The launch (B8, the mock's launch). The tile flies up and out through the hinge over flightMs on flightEasing, growing to
    // flightScale, until its top is flightBeyond px above the screen's; bumpAtMs after the press the case on the Stage bumps (bumpMs,
    // ease-out, to bumpScale and up bumpRise); at takeoverAtMs the console's colour takes the Stage over through a circle from
    // (takeoverX, takeoverY), the case's middle, to takeoverRadius over takeoverMs on takeoverEasing, the title's letters land (HeadingIn,
    // from titleFromEm) and the Launcher floods to the in-game colour; its grid goes over gridFadeMs and companionAtMs after the press
    // the companion builds. Under reduced motion the takeover is simply there.
    Launch:    { flightMs: 340, takeoverMs: 640, flightEasing: 'cubic-bezier(.6, 0, .9, .5)', flightScale: 1.25, flightBeyond: 340,
                 bumpAtMs: 270, bumpMs: 420, bumpScale: 1.16, bumpRise: 20, takeoverAtMs: 430, takeoverEasing: 'cubic-bezier(.3, .6, .2, 1)',
                 takeoverX: 1484, takeoverY: 516, takeoverRadius: 2200, titleFromEm: 0.8, gridFadeMs: 280, companionAtMs: 710 },
    // The companion (B8): its parts arrive (InGameIn, the mock's launch: rising `rise` px, stagger ms apart, on the Land curve); a tab
    // slides in from the side you moved to (InGameTab); when the game ends its parts drop out (InGameOut, on the Leave curve), the
    // Stage's colour fades over launchFadeMs, and after holdMs the ground floods back and the tiles pop in, the ring popAfterMs later.
    // An unlock holds the Stage's line on it for UnlockLine.holdMs.
    InGameIn:  { ms: 460, stagger: 55, rise: 80 },
    InGameTab: { ms: 420, slide: 120 },
    InGameOut: { ms: 200, stagger: 18, drop: 60, launchFadeMs: 320, holdMs: 340, popAfterMs: 130 },
    UnlockLine: { holdMs: 3200 },
    SwapScreens: { ms: 650 },
    IdleHop:   { afterMs: 4800, everyMs: 6500, ms: 560, staggerPerTile: 75, rise: 20,
                 letterDelay: 260, letterStagger: 45, letterRiseEm: 0.07 },   // the name's letters hop too, after the tiles
    MediumSway: { periodMs: 14000, fromDeg: 32, toDeg: 14 },
    RingBreath: { periodMs: 2200, scale: 1.03 },
    Message:   { holdMs: 2800, errorHoldMs: 4200, shakeMs: 380, shake: 22, shakeBack: 18, shakeSettle: 12 },   // an error shakes -22, +18, -12, 0
    InGameRest: { afterMs: 9000, fadeMs: 1200 },
    SessionAchievementBar: { ms: 800, easing: 'cubic-bezier(.2, 1.3, .3, 1)' },   // B8: the mock's .pbar transition
  },

  // How the Stage keeps its text readable over a moving backdrop. Three zones: the left side (console name,
  // the big name, the two lines under it) and the two right corners. For each: how strong a veil white ink
  // would need, how strong a veil dark ink would need, take whichever needs less, and lay only that much.
  // legibility.json holds the answer baked per backdrop from the prototypes.
  legibility: {
    Target: 3.8,            // the contrast the rule aims for
    Pass: 3.0,              // the contrast the stress sheet demands
    BaseVeilLeft: 0.28, BaseVeilCorner: 0.25, MaxVeil: 0.94,
    LeftVeilPastName: 32,   // px beyond the name box before the veil starts to fade
    LeftVeilFade: 0.24,     // of the screen's width
    Brightest: 0.96, Darkest: 0.04,   // the percentiles of the zone's pixels the rule reads
    SwitchInkMargin: 0.10,  // how much less veil the other ink must need before the ink changes
    RecheckMs: 450,
  },
};
