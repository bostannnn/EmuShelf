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
  },

  // The Launcher, 1240x1080.
  launcher: {
    HomeColumns: 5, HomeRows: 4, HomeLeft: 108, HomeTop: 44, HomePitch: 212,
    FolderRows: 3, FolderColumnsVisible: 4, FolderLeft: 162, FolderTop: 208, FolderPitch: 236,
    HeaderLeft: 162, HeaderTop: 40, HeaderHeight: 136,
    DockBottom: 32, DockGap: 24,
    RowPitch: 88, RowPitchPanel: 92, RowPitchSettings: 104, PanelRowsVisible: 7,
    KeyPitch: 112, KeyboardLeft: 66, KeyboardTop: 424,
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
    ProgressHeight: 12,
  },

  colour: {
    OnGround: '#FFFFFF',                   // type and glyphs on the Launcher
    TileRest: 'rgba(255,255,255,0.17)',    // a folder tile, a dock tile, a key
    TileFocus: '#FFFFFF',                  // the focused one; its ink is the console's colour
    Ring: '#FFFFFF',
    Danger: '#FFC9BD',
    RowRestOpacity: 0.58, RowLockedOpacity: 0.34, EmptySlotDotOpacity: 0.30, MissingTileOpacity: 0.45,
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
    TypedLetterIn: { ms: 460, fromEm: 0.55, tilt: -8 },
    DigitIn:   { ms: 400, delay: 50, stagger: 12, travelEm: 1.1 },
    DigitOut:  { ms: 220, stagger: 12, travelEm: 1.1 },
    Ring:      { ms: 300, coversBothAt: 0.38 },
    FocusLift: { ms: 380 },
    Flood:     { ms: 640, easing: 'cubic-bezier(.2, .6, .2, 1)', radius: 1640 },
    Wipe:      { ms: 760, delay: 110, easing: 'cubic-bezier(.2, .6, .2, 1)', radius: 2300, below: 40 },   // from the focused tile's x, below the Stage's bottom edge
    Scroll:    { ms: 500, easing: 'cubic-bezier(.2, 1.15, .3, 1)' },
    FolderOut: { ms: 250, staggerPerTile: 20 },
    FolderDeal: { ms: 480, staggerPerTile: 45, rise: 160 },
    RowsIn:    { ms: 420, stagger: 32, slide: 140 },
    Launch:    { flightMs: 340, takeoverMs: 640 },
    SwapScreens: { ms: 650 },
    IdleHop:   { afterMs: 4800, everyMs: 6500, ms: 560, staggerPerTile: 75, rise: 20,
                 letterDelay: 260, letterStagger: 45, letterRiseEm: 0.07 },   // the name's letters hop too, after the tiles
    MediumSway: { periodMs: 14000, fromDeg: 32, toDeg: 14 },
    RingBreath: { periodMs: 2200, scale: 1.03 },
    Message:   { holdMs: 2800, errorHoldMs: 4200, shakeMs: 380, shake: 22 },
    InGameRest: { afterMs: 9000, fadeMs: 1200 },
    SessionAchievementBar: { ms: 800 },
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
