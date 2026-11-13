using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Avalonia.VisualTree;
using EmuShelf.App.Services;
using EmuShelf.App.ViewModels;
using EmuShelf.App.Views;
using EmuShelf.App.Views.Handheld;
using EmuShelf.Core.Achievements;
using EmuShelf.Core.Handheld;
using EmuShelf.Core.Launching;
using EmuShelf.Core.Library;
using EmuShelf.Core.Metadata;
using EmuShelf.Core.SecondScreen;
using EmuShelf.Infrastructure.Library;
using EmuShelf.Infrastructure.Persistence;
using EmuShelf.Infrastructure.Storage;
using EmuShelf.Integrations.Systems;
using Xunit;

namespace EmuShelf.App.Tests.Handheld;

// Design-system catalog frames for Handheld mode (the AYN Thor's two screens), not assertions. The real Launcher
// (bottom screen, 1240×1080) and Stage (top screen, 1920×1080) are hosted headlessly over a sample library of real
// platforms with plausible titles and stand-in covers, and every major state is written out as a top/bottom pair.
// It covers what the H4–H6 snapshot tests do not, with one consistent sample library across every frame.
//
// Every test returns at once unless EMUSHELF_DESIGN_SNAPSHOT_DIR is set, so a normal `dotnet test` stays fast. Run with
//   EMUSHELF_DESIGN_SNAPSHOT_DIR=/some/dir dotnet test tests/EmuShelf.App.Tests --filter "FullyQualifiedName~DesignSnapshot"
// into a fresh directory each time. The frames are in today's look; EMUSHELF_DESIGN_LOOK=BigType renders the same states in
// the Big Type look (M47 B2) instead, into a directory of its own:
//   EMUSHELF_DESIGN_LOOK=BigType EMUSHELF_DESIGN_SNAPSHOT_DIR=/other/dir dotnet test tests/EmuShelf.App.Tests --filter "FullyQualifiedName~DesignSnapshot"
// It is a part of HandheldShellViewModelTests so it can reuse that class's fake library
// and pad helpers.
//
// M47 B0: two renders on one machine are pixel-identical, so B1 can prove its token move changed nothing
// (HandheldDesignFrameComparisonTests compares two directories). What would differ between runs is pinned here, never
// tolerated in the comparison: the wall clock (DesignClock: a fixed Thursday evening in UTC+3, with real timers), the
// culture (en-GB), the backdrop's drift (HandheldEraBackdrop.DriftClock stands still, so the blocks and the bar sit where
// their loop starts), indeterminate progress bars (held at 40%, DesignHoldProgress), every transition and spring on real
// time (a frame is written only once both screens have stopped changing, DesignRestAsync), and the compositor's own
// leftovers (each frame is drawn afresh from the visual tree, DesignCapture). M47 B3: the build's commit and its date
// (Settings › About) are written as fixed text (DesignHoldBuild), so renders made on different commits compare too.
public partial class HandheldShellViewModelTests
{
    private static string? DesignOutput =>
        Environment.GetEnvironmentVariable("EMUSHELF_DESIGN_SNAPSHOT_DIR") is { Length: > 0 } directory ? directory : null;

    /// <summary>The look the frames are rendered in: today's, unless EMUSHELF_DESIGN_LOOK names another (M47 B2).</summary>
    private static HandheldLook DesignLook =>
        Environment.GetEnvironmentVariable("EMUSHELF_DESIGN_LOOK") is { Length: > 0 } name
            ? Enum.Parse<HandheldLook>(name, ignoreCase: true)
            : HandheldLook.Classic;

    // ---- The sample library ----

    private sealed record DesignTitle(
        string Title,
        bool Cover = true,
        bool Available = true,
        int PlayedHoursAgo = -1,
        int Plays = 0,
        int Minutes = 0,
        string? Achievements = null,
        double Ratio = 0,
        int Discs = 1,
        bool AsksScreen = false,
        bool Fanart = false,
        bool Logo = false);

    private static readonly (string SystemId, string Emulator, DesignTitle[] Titles)[] DesignShelf =
    [
        ("playstation2", "NetherSX2",
        [
            new("Burnout 3: Takedown"),
            new("Final Fantasy X", PlayedHoursAgo: 30, Plays: 12, Minutes: 2710, Achievements: "31 / 74", Ratio: 0.42),
            new("God of War", PlayedHoursAgo: 140, Plays: 3, Minutes: 380),
            new("Gran Turismo 4", Available: false, PlayedHoursAgo: 200, Plays: 3, Minutes: 410),
            new("Ico", PlayedHoursAgo: 5, Plays: 4, Minutes: 520, Achievements: "6 / 20", Ratio: 0.3),
            new("Jak and Daxter: The Precursor Legacy"),
            new("Katamari Damacy", Cover: false),
            new("Kingdom Hearts"),
            new("Metal Gear Solid 3: Snake Eater"),
            new("Okami", PlayedHoursAgo: 1, Plays: 9, Minutes: 1335, Achievements: "9 / 24", Ratio: 0.375, Fanart: true),
            new("Persona 4"),
            new("Ratchet & Clank"),
            new("Shadow of the Colossus", PlayedHoursAgo: 52, Plays: 2, Minutes: 300, Fanart: true, Logo: true),
            new("SSX Tricky"),
        ]),
        ("gamecube", "Dolphin",
        [
            new("Animal Crossing"),
            new("F-Zero GX"),
            new("Luigi's Mansion"),
            new("Metroid Prime", PlayedHoursAgo: 3, Plays: 6, Minutes: 760, Achievements: "14 / 50", Ratio: 0.28),
            new("Paper Mario: The Thousand-Year Door"),
            new("Pikmin", Cover: false),
            new("Resident Evil 4"),
            new("Super Mario Sunshine"),
            new("Super Smash Bros. Melee", PlayedHoursAgo: 26, Plays: 21, Minutes: 900),
            new("The Legend of Zelda: The Wind Waker"),
        ]),
        ("wii", "Dolphin",
        [
            new("Mario Kart Wii", PlayedHoursAgo: 48, Plays: 15, Minutes: 640),
            new("Metroid Prime 3: Corruption"),
            new("Super Mario Galaxy", PlayedHoursAgo: 8, Plays: 5, Minutes: 610, Achievements: "22 / 60", Ratio: 0.37),
            new("The Last Story"),
            new("Wii Sports"),
            new("Xenoblade Chronicles"),
        ]),
        ("playstation", "DuckStation",
        [
            new("Castlevania: Symphony of the Night"),
            new("Crash Bandicoot"),
            new("Final Fantasy VII", PlayedHoursAgo: 70, Plays: 30, Minutes: 3120, Achievements: "18 / 52", Ratio: 0.35, Discs: 3),
            new("Gran Turismo 2"),
            new("Metal Gear Solid", AsksScreen: true),
            new("Resident Evil 2"),
            new("Silent Hill"),
            new("Spyro the Dragon"),
            new("Tony Hawk's Pro Skater 2"),
        ]),
        ("playstation3", "aPS3e",
        [
            new("Demon's Souls"),
            new("Journey", PlayedHoursAgo: 96, Plays: 2, Minutes: 150),
            new("Metal Gear Solid 4"),
            new("Persona 5"),
            new("The Last of Us"),
            new("Uncharted 2: Among Thieves"),
        ]),
        ("psp", "PPSSPP",
        [
            new("Crisis Core: Final Fantasy VII"),
            new("Daxter"),
            new("Lumines"),
            new("Monster Hunter Freedom Unite", PlayedHoursAgo: 12, Plays: 40, Minutes: 4300),
            new("Patapon"),
            new("Persona 3 Portable"),
        ]),
        ("3ds", "Azahar",
        [
            new("Animal Crossing: New Leaf"),
            new("Fire Emblem Awakening", PlayedHoursAgo: 20, Plays: 7, Minutes: 1100),
            new("Kid Icarus: Uprising"),
            new("Pokémon X"),
            new("The Legend of Zelda: A Link Between Worlds"),
        ]),
        ("nds", "melonDS",
        [
            new("Chrono Trigger"),
            new("Mario Kart DS"),
            new("Phoenix Wright: Ace Attorney"),
            new("The World Ends with You"),
        ]),
    ];

    /// <summary>
    /// The wall clock every frame reads: Thursday 12 November 2026, 18:00 in UTC+3 (the status time, "played an hour
    /// ago", the Title page's dates, the play log). Timestamps and timers stay real, so delays, holds and the
    /// held-direction rule behave as on the device.
    /// </summary>
    private sealed class DesignClock : TimeProvider
    {
        public static readonly DateTimeOffset Now = DateTimeOffset.Parse("2026-11-12T18:00:00+03:00", CultureInfo.InvariantCulture);
        public static readonly TimeZoneInfo Zone = TimeZoneInfo.CreateCustomTimeZone("UTC+3", TimeSpan.FromHours(3), "UTC+3", "UTC+3");
        public static readonly DesignClock Instance = new();

        public override DateTimeOffset GetUtcNow() => Now.ToUniversalTime();

        public override TimeZoneInfo LocalTimeZone => Zone;
    }

    /// <summary>A clock that never moves on: the backdrop's drift reads it during a design render.</summary>
    private sealed class DesignStillClock : TimeProvider
    {
        public override long GetTimestamp() => 0;
    }

    /// <summary>The play log behind the Activity frames: a real library on disk with sessions from the sample shelf.</summary>
    private sealed class DesignPlayLog : IDisposable
    {
        private readonly string _directory = Path.Combine(Path.GetTempPath(), "EmuShelfHandheldDesign", Guid.NewGuid().ToString("N"));
        private readonly GameLibrary _library;

        public DesignPlayLog()
        {
            var paths = new AppPaths(_directory);
            paths.EnsureDirectoriesExist();
            var database = new LibraryDatabase(paths);
            database.Initialize();
            _library = new GameLibrary(database, new RelativePathResolver(paths));
            var now = DesignClock.Now;
            // In the order they were played (started hours ago, minutes), as the app records them.
            var sessions = new (string Title, string SystemId, double StartedHoursAgo, int Minutes)[]
            {
                ("Monster Hunter Freedom Unite", "psp", 24 * 8, 95),
                ("Final Fantasy VII", "playstation", 72, 120),
                ("Super Smash Bros. Melee", "gamecube", 27, 40),
                ("Final Fantasy X", "playstation2", 31.5, 85),
                ("Super Mario Galaxy", "wii", 9, 55),
                ("Ico", "playstation2", 5.75, 40),
                ("Metroid Prime", "gamecube", 3.75, 35),
                ("Okami", "playstation2", 1.75, 45),
            };
            var ids = new Dictionary<string, long>(StringComparer.Ordinal);
            foreach (var (title, systemId, startedHoursAgo, minutes) in sessions)
            {
                if (!ids.TryGetValue(title, out var id))
                {
                    _library.AddGames([new Game { SystemId = systemId, Path = $"/g/{title}.iso", Title = title, DateAdded = now.AddDays(-60) }]);
                    id = _library.GetGames().Single(game => game.Title == title).Id;
                    ids[title] = id;
                }
                var session = _library.RecordLaunchStarted(id, now.AddHours(-startedHoursAgo))!.Value;
                _library.CompletePlaySession(session, TimeSpan.FromMinutes(minutes), PlaySessionMeasurement.TrackedProcess);
            }
        }

        public ActivityViewModel Create() => new(_library, DesignClock.Instance, DesignClock.Zone, DayOfWeek.Monday);

        public void Dispose()
        {
            try { Directory.Delete(_directory, recursive: true); } catch (IOException) { }
        }
    }

    private sealed class DesignDeviceStatus : IDeviceStatusService
    {
        public DeviceStatus GetStatus() => new(76, IsCharging: false, IsWifiConnected: true);

        public event Action? Changed
        {
            add { }
            remove { }
        }
    }

    private sealed class DesignData
    {
        public required FakeLibrary Library { get; init; }
        public required Dictionary<string, long> Ids { get; init; }
        public required InMemoryHandheldHomeLayoutStore Store { get; init; }
        public required SecondScreenViewModel Companion { get; init; }
        public required DirectoryInfo Media { get; init; }
        public required DesignPlayLog PlayLog { get; init; }

        public long Id(string title) => Ids[title];
    }

    private static DesignData DesignBuild(bool empty = false)
    {
        var library = new FakeLibrary();
        var ids = new Dictionary<string, long>(StringComparer.Ordinal);
        var titles = new Dictionary<long, DesignTitle>();
        var now = DesignClock.Now;
        if (!empty)
        {
            for (var p = 0; p < DesignShelf.Length; p++)
            {
                var (systemId, emulator, shelf) = DesignShelf[p];
                var system = KnownSystems.All.Single(candidate => candidate.Id == systemId);
                library.Platforms.Add(new HandheldPlatform(system.Id, system.Name, system.ShortName, system.AccentColor, shelf.Length,
                    system.Era, system.Backdrops, system.GroundColor));
                library.Games[system.Id] = shelf.Select((title, i) =>
                {
                    var id = ((p + 1) * 100) + i + 1;
                    ids[title.Title] = id;
                    titles[id] = title;
                    library.Details[id] = new HandheldTitleDetails(emulator, Enumerable.Range(1, title.Discs).ToList(), 1,
                        title.AsksScreen ? LaunchScreenDecision.Prompt : LaunchScreenDecision.BuiltIn,
                        HasSecondScreen: true, IsDualScreenSystem: system.IsDualScreen,
                        // From the play log (M46 P4): Final Fantasy VII was first played before the log began.
                        FirstPlayedAt: title.Plays > 0 && title.Title != "Final Fantasy VII" ? now.AddDays(-9 * title.Plays) : null,
                        PlayedBeforeLog: title.Title == "Final Fantasy VII");
                    return new HandheldGameEntry(id, title.Title, system.Id, system.Name,
                        title.PlayedHoursAgo >= 0 ? now.AddHours(-title.PlayedHoursAgo) : null, title.Plays,
                        TimeSpan.FromMinutes(title.Minutes), title.Available, title.Achievements, null, title.Ratio);
                }).ToList();
            }
        }

        // Rendered up front on the UI thread; the fake hands out a fresh decode per request (the shell owns and disposes it).
        var covers = new Dictionary<long, byte[]>();
        var art = new Dictionary<(long, HandheldArtKind), byte[]>();
        var palette = 0;
        foreach (var game in library.Games.Values.SelectMany(games => games))
        {
            var title = titles[game.Id];
            if (title.Cover)
                covers[game.Id] = DesignCoverPng(game.Title, game.SystemId, 360, (palette++ * 5) + 3);
            if (title.Fanart)
                art[(game.Id, HandheldArtKind.Fanart)] = DesignFanartPng(game.Title);
            if (title.Logo)
                art[(game.Id, HandheldArtKind.Logo)] = DesignLogoPng(game.Title);
        }
        library.CoverMaker = game => covers.TryGetValue(game.Id, out var png) ? new Bitmap(new MemoryStream(png)) : null;
        library.ArtMaker = (game, kind) => art.TryGetValue((game.Id, kind), out var png) ? new Bitmap(new MemoryStream(png)) : null;
        library.SettingsFactory = CreateSettingsProjection;
        library.SetupFactory = CreateSetupProjection;
        library.PickedFolder = "/storage/emulated/0/ROMs/PS2";
        var playLog = new DesignPlayLog();
        library.ActivityFactory = playLog.Create;

        var store = new InMemoryHandheldHomeLayoutStore();
        if (!empty)
        {
            store.Save(HandheldHomeLayout.FromSlots(
            [
                HandheldSlot.Recent, HandheldSlot.PlatformFolder("playstation2"), HandheldSlot.PlatformFolder("gamecube"),
                HandheldSlot.PlatformFolder("wii"), HandheldSlot.PlatformFolder("playstation"),
                HandheldSlot.PlatformFolder("playstation3"), HandheldSlot.PlatformFolder("psp"), HandheldSlot.PlatformFolder("3ds"),
                HandheldSlot.PlatformFolder("nds", isNew: true), HandheldSlot.Settings,
                HandheldSlot.UserFolder("coop"), HandheldSlot.Game(ids["Ico"]), HandheldSlot.Game(ids["Super Mario Galaxy"]),
                HandheldSlot.Game(ids["Gran Turismo 4"]), HandheldSlot.Activity,
            ],
            [
                new HandheldUserFolder("coop", "Couch co-op", "#3BBD79",
                    [ids["Mario Kart Wii"], ids["Super Smash Bros. Melee"], ids["Wii Sports"], ids["Mario Kart DS"], ids["Burnout 3: Takedown"]]),
            ]));
        }

        var media = Directory.CreateTempSubdirectory("emushelf-design-");
        return new DesignData
        {
            Library = library,
            Ids = ids,
            Store = store,
            Companion = DesignCompanion(media.FullName),
            Media = media,
            PlayLog = playLog,
        };
    }

    /// <summary>The companion (Screen 2's model): five pinned apps, the drawer's apps, an achievement set and photos.</summary>
    private static SecondScreenViewModel DesignCompanion(string mediaDirectory)
    {
        var model = new SecondScreenViewModel();
        var dock = new (string Label, string Colour, string Letter)[]
        {
            ("Browser", "#2F80ED", "B"), ("Music", "#E5484D", "♪"), ("YouTube", "#D93025", "▶"), ("Discord", "#5865F2", "D"),
            ("Files", "#F2A93B", "F"),
        };
        for (var i = 0; i < dock.Length; i++)
        {
            model.Dock[i].Label = dock[i].Label;
            model.Dock[i].Icon = DesignIcon(dock[i].Colour, dock[i].Letter);
            model.Dock[i].AccentColor = Color.Parse(dock[i].Colour);
        }
        var apps = new (string Label, string Colour, string Letter)[]
        {
            ("Browser", "#2F80ED", "B"), ("Calculator", "#4F5D75", "="), ("Calendar", "#E5484D", "31"), ("Camera", "#3A3F4B", "◉"),
            ("Clock", "#1F2937", "◷"), ("Discord", "#5865F2", "D"), ("Dolphin", "#3B82C4", "Dl"), ("DuckStation", "#E0A526", "Du"),
            ("Files", "#F2A93B", "F"), ("Gallery", "#10B981", "G"), ("Music", "#E5484D", "♪"), ("NetherSX2", "#2563EB", "N"),
            ("PPSSPP", "#F59E0B", "P"), ("Settings", "#6B7280", "⚙"), ("Steam Link", "#1B2838", "S"), ("YouTube", "#D93025", "▶"),
            ("Azahar", "#C0568E", "A"), ("melonDS", "#7580B9", "m"),
        };
        foreach (var (label, colour, letter) in apps)
            model.Apps.Add(new SecondScreenAppViewModel($"design/{label}", label, DesignIcon(colour, letter)));

        var names = new (string Title, string Description, int Points)[]
        {
            ("Midgar Escape", "Leave Midgar for the first time", 5), ("Chocobo Breeder", "Breed a gold chocobo", 25),
            ("Limit Break", "Learn a level 4 limit break", 10), ("Golden Saucer", "Win the Speed Square jackpot", 10),
            ("Materia Hunter", "Master any materia", 10), ("Knights of the Round", "Obtain the Knights of the Round", 25),
            ("Ruby Weapon", "Defeat Ruby Weapon", 50), ("Emerald Weapon", "Defeat Emerald Weapon", 50),
            ("Snowboarder", "Finish the snowboard course", 5), ("Fort Condor", "Win every Fort Condor battle", 10),
            ("Gil Collector", "Hold 1,000,000 gil", 10), ("The Planet's Cry", "Reach the Northern Crater", 25),
        };
        var badgeColours = new[] { "#D9573B", "#3B8ED9", "#4CB36B", "#B34CA8", "#E0B03A", "#3BB2D9" };
        var rows = names.Select((entry, i) => new AchievementRowViewModel(
            new RetroAchievementsAchievement(i + 1, entry.Title, entry.Description, entry.Points, string.Empty, i + 1,
                // Noon UTC, so the date reads the same in any time zone the render runs in.
                i < 5 ? new DateTimeOffset(DesignClock.Now.UtcDateTime.Date.AddHours(12), TimeSpan.Zero).AddDays(-(10 - i)) : null, null),
            badges: null, loadBadge: false)).ToList();
        foreach (var (row, i) in rows.Select((row, i) => (row, i)))
            row.Badge = DesignBitmap(DesignBadgeFace(badgeColours[i % badgeColours.Length], i < 5), 64, 64);
        model.SetAchievements(rows);
        model.AchievementsSummary = "5 / 12 · 60 pts";
        model.SetMedia(new CompanionMediaSet(1, "Final Fantasy VII",
        [
            new CompanionMediaItem("Screenshot", GameMediaKind.Screenshot,
                DesignSavePng(mediaDirectory, "shot-0", DesignFanartPng("Midgar"))),
            new CompanionMediaItem("Screenshot", GameMediaKind.Screenshot,
                DesignSavePng(mediaDirectory, "shot-1", DesignFanartPng("Cosmo Canyon"))),
            new CompanionMediaItem("Gameplay video", GameMediaKind.Video, null),
            new CompanionMediaItem("Title screen", GameMediaKind.TitleScreen,
                DesignSavePng(mediaDirectory, "shot-2", DesignFanartPng("Final Fantasy VII"))),
        ]));
        model.CanRefresh = true;
        model.CanFetchMedia = true;
        return model;
    }

    // ---- Stand-in artwork ----

    private static readonly (string Top, string Bottom)[] DesignGradients =
    [
        ("#F2994A", "#8E2DE2"), ("#56CCF2", "#2F4FB5"), ("#6FCF97", "#1E5631"), ("#EB5757", "#4A0D2B"),
        ("#F2C94C", "#B0562A"), ("#BB6BD9", "#2E1A5E"), ("#2D9CDB", "#0B2545"), ("#D7DEE6", "#4F5B66"),
        ("#FF8A65", "#6D214F"), ("#4DD0E1", "#00594F"), ("#C8A27C", "#3E2723"), ("#90CAF9", "#283593"),
    ];

    private static (Color Top, Color Bottom) DesignColours(string seed)
    {
        var hash = seed.Aggregate(17, (sum, c) => unchecked((sum * 31) + c));
        var (top, bottom) = DesignGradients[(hash & 0x7FFFFFFF) % DesignGradients.Length];
        return (Color.Parse(top), Color.Parse(bottom));
    }

    private static byte[] DesignPng(Control face, int width, int height)
    {
        using var bitmap = DesignBitmap(face, width, height);
        using var stream = new MemoryStream();
        bitmap.Save(stream, PngBitmapEncoderOptions.Default);
        return stream.ToArray();
    }

    private static RenderTargetBitmap DesignBitmap(Control face, int width, int height)
    {
        face.Width = width;
        face.Height = height;
        face.Measure(new Size(width, height));
        face.Arrange(new Rect(0, 0, width, height));
        var target = new RenderTargetBitmap(new PixelSize(width, height));
        target.Render(face);
        return target;
    }

    private static string DesignSavePng(string directory, string name, byte[] png)
    {
        var path = Path.Combine(directory, name + ".png");
        File.WriteAllBytes(path, png);
        return path;
    }

    /// <summary>
    /// A stand-in box cover at the platform's real cover ratio: a two-colour gradient, a dark platform band and the title
    /// near the top (tiles crop covers from the top, as box art keeps its logo there). <paramref name="palette"/> picks the
    /// gradient; by default it follows the title.
    /// </summary>
    private static byte[] DesignCoverPng(string title, string systemId, int width, int? palette = null)
    {
        var system = KnownSystems.All.Single(candidate => candidate.Id == systemId);
        var height = (int)Math.Round(width / system.CoverAspectRatio);
        var (top, bottom) = palette is { } index
            ? (Color.Parse(DesignGradients[index % DesignGradients.Length].Top), Color.Parse(DesignGradients[index % DesignGradients.Length].Bottom))
            : DesignColours(title);
        var shortSide = Math.Min(width, height);
        var face = new Grid
        {
            Background = new LinearGradientBrush
            {
                StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative),
                EndPoint = new RelativePoint(0.4, 1, RelativeUnit.Relative),
                GradientStops = { new GradientStop(top, 0), new GradientStop(bottom, 1) },
            },
            RowDefinitions = new RowDefinitions("Auto,*"),
            ClipToBounds = true,
            Children =
            {
                new Avalonia.Controls.Shapes.Ellipse
                {
                    [Grid.RowProperty] = 1,
                    Width = shortSide * 0.95,
                    Height = shortSide * 0.95,
                    HorizontalAlignment = HorizontalAlignment.Right,
                    VerticalAlignment = VerticalAlignment.Bottom,
                    Margin = new Thickness(0, 0, -shortSide * 0.3, -shortSide * 0.25),
                    Fill = new SolidColorBrush(Colors.White, 0.16),
                },
                new Border
                {
                    Background = new SolidColorBrush(Color.Parse("#E6111318")),
                    Padding = new Thickness(shortSide * 0.1, shortSide * 0.03),
                    Child = new TextBlock
                    {
                        Text = system.Name.ToUpperInvariant(),
                        Foreground = Brushes.White,
                        FontSize = Math.Max(11, shortSide * 0.05),
                        FontWeight = FontWeight.SemiBold,
                    },
                },
                new TextBlock
                {
                    [Grid.RowProperty] = 1,
                    Text = title,
                    Foreground = Brushes.White,
                    FontSize = shortSide * (title.Length > 24 ? 0.095 : title.Length > 13 ? 0.115 : 0.15),
                    FontWeight = FontWeight.Black,
                    TextWrapping = TextWrapping.Wrap,
                    VerticalAlignment = VerticalAlignment.Top,
                    Margin = new Thickness(shortSide * 0.1, shortSide * 0.07, shortSide * 0.1, 0),
                },
            },
        };
        return DesignPng(face, width, height);
    }

    /// <summary>A stand-in 16:9 fan art: a dusk gradient with soft shapes, no text.</summary>
    private static byte[] DesignFanartPng(string seed)
    {
        const int width = 1600, height = 900;
        var (top, bottom) = DesignColours(seed + "-art");
        var face = new Grid
        {
            Background = new LinearGradientBrush
            {
                StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative),
                EndPoint = new RelativePoint(1, 1, RelativeUnit.Relative),
                GradientStops = { new GradientStop(top, 0), new GradientStop(bottom, 1) },
            },
            ClipToBounds = true,
            Children =
            {
                new Avalonia.Controls.Shapes.Ellipse
                {
                    Width = 900, Height = 900, HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Top,
                    Margin = new Thickness(0, -300, -200, 0), Fill = new SolidColorBrush(Colors.White, 0.18),
                },
                new Avalonia.Controls.Shapes.Ellipse
                {
                    Width = 1400, Height = 700, HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Bottom,
                    Margin = new Thickness(-300, 0, 0, -380), Fill = new SolidColorBrush(Colors.Black, 0.28),
                },
                new Avalonia.Controls.Shapes.Ellipse
                {
                    Width = 220, Height = 220, HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top,
                    Margin = new Thickness(320, 160, 0, 0), Fill = new SolidColorBrush(Color.Parse("#FFF4D6"), 0.7),
                },
            },
        };
        return DesignPng(face, width, height);
    }

    /// <summary>A stand-in transparent logo: the title set large, white with a dark edge.</summary>
    private static byte[] DesignLogoPng(string title)
    {
        var face = new Panel
        {
            Children =
            {
                new TextBlock
                {
                    Text = title.ToUpperInvariant(),
                    Foreground = Brushes.White,
                    FontSize = 68,
                    FontWeight = FontWeight.Black,
                    TextWrapping = TextWrapping.Wrap,
                    TextAlignment = TextAlignment.Center,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center,
                    Effect = new DropShadowEffect { BlurRadius = 12, OffsetX = 0, OffsetY = 3, Color = Colors.Black, Opacity = 0.7 },
                },
            },
        };
        return DesignPng(face, 900, 300);
    }

    private static Control DesignIconFace(string colour, string letter) => new Border
    {
        Background = new LinearGradientBrush
        {
            StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative),
            EndPoint = new RelativePoint(0, 1, RelativeUnit.Relative),
            GradientStops =
            {
                new GradientStop(Color.Parse(colour), 0),
                new GradientStop(Color.Parse(colour) is var c ? Color.FromRgb((byte)(c.R * 0.72), (byte)(c.G * 0.72), (byte)(c.B * 0.72)) : default, 1),
            },
        },
        CornerRadius = new CornerRadius(24),
        Child = new TextBlock
        {
            Text = letter,
            Foreground = Brushes.White,
            FontSize = letter.Length > 1 ? 34 : 46,
            FontWeight = FontWeight.Bold,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
        },
    };

    private static Bitmap DesignIcon(string colour, string letter) => DesignBitmap(DesignIconFace(colour, letter), 96, 96);

    private static Control DesignBadgeFace(string colour, bool earned) => new Border
    {
        Background = new SolidColorBrush(Color.Parse(earned ? colour : "#5B6270")),
        Child = new Avalonia.Controls.Shapes.Ellipse
        {
            Width = 34, Height = 34, Fill = new SolidColorBrush(Colors.White, earned ? 0.85 : 0.35),
            HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center,
        },
    };

    // Web cover search: six results, each previewed with a stand-in cover.
    private sealed class DesignCoverResults(string title) : IGameArtworkSearchProvider
    {
        public Task<IReadOnlyList<ArtworkSearchResult>> SearchAsync(string query, string systemName, double preferredAspectRatio,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<ArtworkSearchResult>>([.. Enumerable.Range(0, 6).Select(i => new ArtworkSearchResult("web-search",
                new Uri($"https://images.example/{i}.png"), new Uri($"https://images.example/{i}-preview.png"),
                new Uri($"https://source.example/{i}"), i % 2 == 0 ? 1000 : 600, i % 2 == 0 ? 1412 : 850, $"{title} cover {i + 1}", ".png"))]);
    }

    private sealed class DesignCoverDownloads(string directory, string title, string systemId) : IRemoteArtworkDownloader
    {
        public async Task<DownloadedArtwork?> DownloadFirstAsync(IReadOnlyList<ArtworkCandidate> candidates, CancellationToken cancellationToken = default)
        {
            var name = Path.GetFileNameWithoutExtension(candidates[0].SourceUri.AbsolutePath);
            var variant = int.Parse(name.Split('-')[0], CultureInfo.InvariantCulture);
            var png = await Dispatcher.UIThread.InvokeAsync(() => DesignCoverPng(title, systemId, 300, (variant * 7) + 1));
            var path = Path.Combine(directory, $"{Guid.NewGuid():N}.png");
            await File.WriteAllBytesAsync(path, png, cancellationToken);
            return new DownloadedArtwork(candidates[0], path);
        }
    }

    // ---- Hosting both screens ----

    private sealed class DesignRig : IDisposable
    {
        public required DesignData Data { get; init; }
        public required HandheldShellViewModel Shell { get; init; }
        public required HandheldLauncherView LauncherView { get; init; }
        public required HandheldStageView StageView { get; init; }
        public required Window LauncherWindow { get; init; }
        public required Window StageWindow { get; init; }
        public required bool Swapped { get; init; }
        public required DesignPins Pins { get; init; }

        public void Dispose()
        {
            LauncherWindow.Close();
            StageWindow.Close();
            Shell.Dispose();
            Data.PlayLog.Dispose();
            Pins.Dispose();
            try
            {
                Data.Media.Delete(recursive: true);
            }
            catch (IOException)
            {
            }
        }
    }

    /// <summary>
    /// What a design render pins while it runs (M47 B0), put back afterwards: the culture the status time and dates are
    /// written in, the backdrop's drift clock (read by every backdrop made from now on), and the look the tokens hold
    /// (<see cref="DesignLook"/>, taken before the views that read them are built, M47 B2; today's look afterwards).
    /// </summary>
    private sealed class DesignPins : IDisposable
    {
        private static readonly CultureInfo Culture = CultureInfo.GetCultureInfo("en-GB");
        private readonly CultureInfo _culture = CultureInfo.CurrentCulture;
        private readonly CultureInfo _uiCulture = CultureInfo.CurrentUICulture;
        private readonly CultureInfo? _defaultCulture = CultureInfo.DefaultThreadCurrentCulture;
        private readonly CultureInfo? _defaultUiCulture = CultureInfo.DefaultThreadCurrentUICulture;
        private readonly TimeProvider _driftClock = HandheldEraBackdrop.DriftClock;

        public DesignPins()
        {
            // Timer callbacks on the UI thread do not carry the test's async context, so the thread's own culture is set too.
            CultureInfo.CurrentCulture = CultureInfo.CurrentUICulture = Culture;
            CultureInfo.DefaultThreadCurrentCulture = CultureInfo.DefaultThreadCurrentUICulture = Culture;
            Thread.CurrentThread.CurrentCulture = Thread.CurrentThread.CurrentUICulture = Culture;
            HandheldEraBackdrop.DriftClock = new DesignStillClock();
            HandheldTokens.Use(DesignLook);
        }

        public void Dispose()
        {
            HandheldTokens.Use(HandheldLook.Classic);
            HandheldEraBackdrop.DriftClock = _driftClock;
            CultureInfo.DefaultThreadCurrentCulture = _defaultCulture;
            CultureInfo.DefaultThreadCurrentUICulture = _defaultUiCulture;
            CultureInfo.CurrentCulture = Thread.CurrentThread.CurrentCulture = _culture;
            CultureInfo.CurrentUICulture = Thread.CurrentThread.CurrentUICulture = _uiCulture;
        }
    }

    private static async Task<DesignRig> DesignRigAsync(bool swapped = false, bool empty = false, bool companion = true)
    {
        var pins = new DesignPins();
        var data = DesignBuild(empty);
        var shell = new HandheldShellViewModel(data.Library, new DesignDeviceStatus(), DesignClock.Instance, data.Store, screensSwapped: swapped)
        {
            SearchDebounce = TimeSpan.Zero,
            Look = DesignLook,
        };
        if (companion)
            shell.Companion = data.Companion;
        await shell.InitializeAsync(TestContext.Current.CancellationToken);
        // Normal: the Launcher on the bottom (1240-wide) screen and the Stage on the top (16:9) one. Swapped (X): the other way round.
        var launcher = new HandheldLauncherView(null, pinnedWide: swapped) { DataContext = shell };
        var stage = new HandheldStageView(null, pinnedNarrow: swapped) { DataContext = shell };
        var (launcherWidth, stageWidth) = swapped ? (1920, 1240) : (1240, 1920);
        var launcherWindow = new Window { Content = launcher, Width = launcherWidth, Height = 1080 };
        var stageWindow = new Window { Content = stage, Width = stageWidth, Height = 1080 };
        launcherWindow.Show();
        stageWindow.Show();
        await DesignSettleAsync();
        return new DesignRig
        {
            Data = data,
            Shell = shell,
            LauncherView = launcher,
            StageView = stage,
            LauncherWindow = launcherWindow,
            StageWindow = stageWindow,
            Swapped = swapped,
            Pins = pins,
        };
    }

    private static async Task DesignSettleAsync(int rounds = 16)
    {
        for (var i = 0; i < rounds; i++)
        {
            await Dispatcher.UIThread.InvokeAsync(() => { }, DispatcherPriority.Background);
            AvaloniaHeadlessPlatform.ForceRenderTimerTick();
            await Task.Delay(35);
        }
    }

    // A frame is at rest once it has not changed for this long, captured every round of DesignSettleAsync (about 40 ms).
    private static readonly TimeSpan DesignRestWindow = TimeSpan.FromMilliseconds(600);
    private static readonly TimeSpan DesignRestTimeout = TimeSpan.FromSeconds(20);

    /// <summary>
    /// Waits until every window's frame has stopped changing and returns those frames (M47 B0). Headless Avalonia runs
    /// transitions, the springs and crossfades on the real clock, so a frame captured after a fixed wait can catch one
    /// mid-flight; waiting them out is what makes two renders identical. Fails, naming the frame, if something never rests.
    /// </summary>
    private static async Task<RenderTargetBitmap[]> DesignRestAsync(IReadOnlyList<Window> windows, string what)
    {
        var frames = new RenderTargetBitmap?[windows.Count];
        var previous = new FramePixels?[windows.Count];
        var waited = Stopwatch.StartNew();
        var still = Stopwatch.StartNew();
        try
        {
            while (true)
            {
                foreach (var window in windows)
                {
                    DesignHoldProgress(window);
                    DesignHoldBuild(window);
                }
                await DesignSettleAsync(rounds: 1);
                var changed = false;
                for (var i = 0; i < windows.Count; i++)
                {
                    var frame = DesignCapture(windows[i]);
                    var pixels = DesignPixels(frame);
                    changed |= previous[i] is not { } before || !FrameComparison.Compare(before, pixels).IsIdentical;
                    previous[i] = pixels;
                    frames[i]?.Dispose();
                    frames[i] = frame;
                }
                if (changed)
                    still.Restart();
                else if (still.Elapsed >= DesignRestWindow)
                    break;
                if (waited.Elapsed > DesignRestTimeout)
                    Assert.Fail($"{what} was still changing after {DesignRestTimeout.TotalSeconds:0} s");
            }
            var result = frames.Select(frame => frame!).ToArray();
            Array.Clear(frames);
            return result;
        }
        finally
        {
            foreach (var frame in frames)
                frame?.Dispose();
        }
    }

    /// <summary>
    /// An indeterminate progress bar (the Title page's launch bar) loops for as long as it shows and never rests, so a
    /// design render draws every one determinate at 40%: the track and the fill in their own colours, the same each run.
    /// </summary>
    private static void DesignHoldProgress(Window window)
    {
        foreach (var bar in window.GetVisualDescendants().OfType<ProgressBar>().Where(bar => bar.IsIndeterminate))
        {
            bar.IsIndeterminate = false;
            bar.Minimum = 0;
            bar.Maximum = 100;
            bar.Value = 40;
        }
    }

    // What the build stamps into the app (AppBuildInfo), and what a design render writes in its place.
    private const string DesignCommit = "0000000";
    private const string DesignCommitDate = "2026-01-01 00:00";

    /// <summary>
    /// Settings › About prints the commit the build came from and that commit's date (M47 B3), so a frame showing them
    /// differed between renders made on two commits. Any text holding either is drawn with fixed stand-ins instead, as
    /// the app formats them (EmulatorSettingsViewModel). The app is not changed: only the harness's frame is.
    /// </summary>
    private static void DesignHoldBuild(Window window)
    {
        var commit = EmuShelf.App.AppBuildInfo.CommitHash;
        var date = EmuShelf.App.AppBuildInfo.CommitDate is { } stamped ? stamped.ToLocalTime().ToString("yyyy-MM-dd HH:mm") : null;
        if (string.IsNullOrEmpty(commit) && date is null)
            return;
        foreach (var text in window.GetVisualDescendants().OfType<TextBlock>())
        {
            if (text.Text is not { Length: > 0 } shown)
                continue;
            var pinned = shown;
            if (!string.IsNullOrEmpty(commit))
                pinned = pinned.Replace(commit, DesignCommit, StringComparison.Ordinal);
            if (date is not null)
                pinned = pinned.Replace(date, DesignCommitDate, StringComparison.Ordinal);
            if (!ReferenceEquals(pinned, shown) && pinned != shown)
                text.Text = pinned;
        }
    }

    /// <summary>
    /// The window drawn afresh from its visual tree, not the compositor's last frame. Two runs left the tree identical
    /// (every bound, transform and shadow bitmap) while the compositor's frames still differed by a few edge pixels on
    /// tiles the selector had passed (M47 B0), so the frame is made from what the controls say. At rest the composition
    /// animations agree with the controls by design (HandheldLauncherMotion), so nothing is lost.
    /// </summary>
    private static RenderTargetBitmap DesignCapture(Window window)
    {
        var size = new PixelSize((int)Math.Round(window.ClientSize.Width), (int)Math.Round(window.ClientSize.Height));
        var frame = new RenderTargetBitmap(size);
        frame.Render(window);
        return frame;
    }

    private static FramePixels DesignPixels(Bitmap frame)
    {
        var pixels = FramePixels.Blank(frame.PixelSize.Width, frame.PixelSize.Height);
        var handle = GCHandle.Alloc(pixels.Pixels, GCHandleType.Pinned);
        try
        {
            frame.CopyPixels(new PixelRect(frame.PixelSize), handle.AddrOfPinnedObject(), pixels.Pixels.Length, pixels.Stride);
        }
        finally
        {
            handle.Free();
        }
        return pixels;
    }

    /// <summary>Writes each window's frame once all of them are at rest: <c>NAME.png</c>, listed in <c>_frames.tsv</c>.</summary>
    private static async Task DesignDumpAsync(params (Window Window, string Name, string Description)[] shots)
    {
        if (DesignOutput is not { } output)
            return;
        Directory.CreateDirectory(output);
        var frames = await DesignRestAsync([.. shots.Select(shot => shot.Window)], string.Join(" + ", shots.Select(shot => shot.Name)));
        try
        {
            for (var i = 0; i < shots.Length; i++)
            {
                var file = shots[i].Name + ".png";
                using (var stream = File.Create(Path.Combine(output, file)))
                    frames[i].Save(stream, PngBitmapEncoderOptions.Default);
                File.AppendAllText(Path.Combine(output, "_frames.tsv"),
                    string.Join('\t', file, $"{frames[i].PixelSize.Width}x{frames[i].PixelSize.Height}", shots[i].Description) + Environment.NewLine);
            }
        }
        finally
        {
            foreach (var frame in frames)
                frame.Dispose();
        }
    }

    /// <summary>Both screens once they are at rest: <c>handheld-NAME-bottom.png</c> and <c>handheld-NAME-top.png</c>.</summary>
    private static async Task DesignPairAsync(DesignRig rig, string name, string description)
    {
        var prefix = rig.Swapped ? "handheld-swapped-" : "handheld-";
        var (bottom, top) = rig.Swapped ? (rig.StageWindow, rig.LauncherWindow) : (rig.LauncherWindow, rig.StageWindow);
        var (bottomWhat, topWhat) = rig.Swapped ? ("Stage", "Launcher") : ("Launcher", "Stage");
        await DesignDumpAsync(
            (bottom, $"{prefix}{name}-bottom", $"{bottomWhat} — {description}"),
            (top, $"{prefix}{name}-top", $"{topWhat} — {description}"));
    }

    private static void DesignFocusFolder(HandheldShellViewModel shell, int index)
    {
        var folder = shell.OpenFolder!;
        for (var guard = 0; guard < 80 && folder.SelectedIndex != index; guard++)
        {
            var (column, row) = (folder.SelectedIndex / HandheldFolderNavigation.Rows, folder.SelectedIndex % HandheldFolderNavigation.Rows);
            var (targetColumn, targetRow) = (index / HandheldFolderNavigation.Rows, index % HandheldFolderNavigation.Rows);
            if (column < targetColumn)
                Press(shell, GamepadAction.NavigateRight);
            else if (column > targetColumn)
                Press(shell, GamepadAction.NavigateLeft);
            else if (row < targetRow)
                Press(shell, GamepadAction.NavigateDown);
            else
                Press(shell, GamepadAction.NavigateUp);
        }
        Assert.Equal(index, folder.SelectedIndex);
    }

    private static int DesignIndexIn(HandheldShellViewModel shell, string title) =>
        shell.OpenFolder!.Items.ToList().FindIndex(tile => tile.Entry.Title == title);

    /// <summary>Moves to the home slot named <paramref name="name"/> on the Launcher's own grid (5×4, or 7×3 on the 16:9 screen).</summary>
    private static void DesignFocusNamed(HandheldShellViewModel shell, string name)
    {
        for (var guard = 0; guard < 40; guard++)
        {
            var slots = shell.CurrentPage!.Slots;
            var columns = slots.Count % 7 == 0 ? 7 : 5;
            var at = slots.Single(slot => slot.IsFocused).Index;
            var target = slots.First(slot => slot.Name == name).Index;
            if (at == target)
                return;
            if (at / columns < target / columns)
                Press(shell, GamepadAction.NavigateDown);
            else if (at / columns > target / columns)
                Press(shell, GamepadAction.NavigateUp);
            else
                Press(shell, at % columns < target % columns ? GamepadAction.NavigateRight : GamepadAction.NavigateLeft);
        }
        Assert.Fail($"Could not reach {name} on home");
    }

    private static void DesignOpenHome(HandheldShellViewModel shell, int slot)
    {
        FocusHome(shell, slot);
        Press(shell, GamepadAction.Confirm);
        Assert.True(shell.IsFolderOpen);
    }

    private static async Task DesignOpenTitleAsync(HandheldShellViewModel shell, string title)
    {
        DesignFocusFolder(shell, DesignIndexIn(shell, title));
        Press(shell, GamepadAction.Confirm);
        Assert.True(shell.IsTitleOpen);
        for (var i = 0; i < 100 && shell.TitlePage!.Details is null; i++)
            await Task.Delay(10);
    }

    private static void DesignCloseAll(HandheldShellViewModel shell)
    {
        for (var i = 0; i < 6 && (shell.IsTitleOpen || shell.IsFolderOpen || shell.IsMenuOpen || shell.Sheet is not null); i++)
            Press(shell, GamepadAction.Cancel);
    }

    // ---- The frames ----

    [AvaloniaFact]
    public async Task DesignSnapshot_HomeDockMenuAndSheets()
    {
        if (DesignOutput is null)
            return;
        using var rig = await DesignRigAsync();
        var shell = rig.Shell;

        await DesignPairAsync(rig, "home-recent-focused", "Home page 1, Recent folder focused (where a start lands)");
        FocusHome(shell, 1);
        await DesignPairAsync(rig, "home-platform-folder-focused", "Home, PlayStation 2 platform folder focused (flap peeks covers)");
        FocusHome(shell, 7);
        await DesignPairAsync(rig, "home-platform-folder-3ds-focused", "Home, Nintendo 3DS folder focused (cartridge-era backdrop)");
        FocusHome(shell, 8);
        await DesignPairAsync(rig, "home-new-folder-focused", "Home, Nintendo DS folder with the New ribbon, focused");
        FocusHome(shell, 9);
        await DesignPairAsync(rig, "home-settings-tile-focused", "Home, Settings app tile focused");
        FocusHome(shell, 10);
        await DesignPairAsync(rig, "home-user-folder-focused", "Home, a user folder (Couch co-op, green) focused");
        FocusHome(shell, 11);
        await DesignPairAsync(rig, "home-pinned-game-focused", "Home, a game pinned to home (Ico) focused");
        FocusHome(shell, 13);
        await DesignPairAsync(rig, "home-pinned-game-missing-focused", "Home, a pinned game whose file is missing (dimmed), focused");
        FocusHome(shell, 14);
        await DesignPairAsync(rig, "home-activity-tile-focused", "Home, the Activity app tile focused");
        FocusHome(shell, 15);
        await DesignPairAsync(rig, "home-empty-slot-focused", "Home, an empty slot focused");

        // The dock: All apps, the five pinned apps, the menu button.
        for (var i = 0; i < 6 && shell.Zone != HandheldZone.Dock; i++)
            Press(shell, GamepadAction.NavigateDown);
        Assert.Equal(HandheldZone.Dock, shell.Zone);
        await DesignPairAsync(rig, "dock-all-apps-focused", "Dock, All apps button focused");
        Press(shell, GamepadAction.NavigateRight, 2);
        await DesignPairAsync(rig, "dock-app-focused", "Dock, a pinned Android app (Music) focused: the Stage sweeps in the app's colour");
        while (shell.DockIndex < shell.DockItems.Count - 1)
            Press(shell, GamepadAction.NavigateRight);
        await DesignPairAsync(rig, "dock-menu-focused", "Dock, menu button focused");

        Press(shell, GamepadAction.Confirm);
        Assert.True(shell.IsMenuOpen);
        await DesignPairAsync(rig, "start-menu", "Start menu open (Search, Add games, Settings, Sounds, Switch to Gamepad, Quit)");
        Press(shell, GamepadAction.NavigateDown, shell.MenuItems.ToList().FindIndex(item => item.Label == "Sounds"));
        await DesignPairAsync(rig, "start-menu-sounds-focused", "Start menu, the Sounds switch row focused");
        Press(shell, GamepadAction.Cancel);
        Press(shell, GamepadAction.NavigateUp);
        Assert.Equal(HandheldZone.Grid, shell.Zone);

        // Y: the actions sheet on a folder and on a pinned game.
        FocusHome(shell, 1);
        Press(shell, GamepadAction.Actions);
        Assert.NotNull(shell.Sheet);
        await DesignPairAsync(rig, "sheet-platform-folder", "Y actions sheet on a platform folder");
        Press(shell, GamepadAction.Cancel);
        FocusHome(shell, 10);
        Press(shell, GamepadAction.Actions);
        await DesignPairAsync(rig, "sheet-user-folder", "Y actions sheet on a user folder");
        Press(shell, GamepadAction.Cancel);
        FocusHome(shell, 11);
        Press(shell, GamepadAction.Actions);
        await DesignPairAsync(rig, "sheet-pinned-game", "Y actions sheet on a game pinned to home");
        Press(shell, GamepadAction.Cancel);

        // The second (empty) page of home.
        Press(shell, GamepadAction.NextPlatform);
        await DesignPairAsync(rig, "home-page-2-empty", "Home page 2 (empty slots), turned with RB");
        Press(shell, GamepadAction.PreviousPlatform);

        // Toasts.
        FocusHome(shell, 1);
        shell.ShowStatus("Added 14 games to PlayStation 2.", StatusSeverity.Info);
        await DesignPairAsync(rig, "toast-info", "Info toast (status line) over home");
        shell.ShowStatus("Scraping covers… 6 of 14", StatusSeverity.Progress);
        await DesignPairAsync(rig, "toast-progress", "Progress toast over home");
        shell.ShowStatus("Could not start NetherSX2: the emulator is not installed. Choose one in Settings → Emulators.", StatusSeverity.Error);
        await DesignPairAsync(rig, "toast-error", "Error toast over home");
    }

    [AvaloniaFact]
    public async Task DesignSnapshot_Folders()
    {
        if (DesignOutput is null)
            return;
        using var rig = await DesignRigAsync();
        var shell = rig.Shell;

        DesignOpenHome(shell, 1);
        await DesignPairAsync(rig, "folder-platform-first-game", "Inside the PlayStation 2 folder: 3-row grid, first game focused");
        DesignFocusFolder(shell, DesignIndexIn(shell, "Katamari Damacy"));
        await DesignPairAsync(rig, "folder-game-without-cover", "PS2 folder, a game with no cover (placeholder art) focused");
        DesignFocusFolder(shell, DesignIndexIn(shell, "Gran Turismo 4"));
        await DesignPairAsync(rig, "folder-game-missing-file", "PS2 folder, a game whose file is missing (dimmed) focused");
        DesignFocusFolder(shell, shell.OpenFolder!.Items.Count - 1);
        await DesignPairAsync(rig, "folder-scrolled-to-end", "PS2 folder scrolled to its last column");
        DesignFocusFolder(shell, 0);
        Press(shell, GamepadAction.NavigateUp);
        Assert.Equal(HandheldZone.Header, shell.Zone);
        await DesignPairAsync(rig, "folder-header-back-focused", "PS2 folder, header Back chip focused");
        Press(shell, GamepadAction.NavigateRight);
        await DesignPairAsync(rig, "folder-header-sort-focused", "PS2 folder, header sort chip (Manual) focused");
        Press(shell, GamepadAction.Confirm);
        Press(shell, GamepadAction.Confirm);
        await DesignPairAsync(rig, "folder-sorted-last-played", "PS2 folder re-sorted to Last played (sort chip focused)");
        Press(shell, GamepadAction.NavigateDown);
        Press(shell, GamepadAction.Actions);
        await DesignPairAsync(rig, "sheet-game-in-folder", "Y actions sheet on a game inside a folder");
        DesignCloseAll(shell);

        DesignOpenHome(shell, 0);
        await DesignPairAsync(rig, "folder-recent", "The Recent folder (recently played, mixed platforms; no sort chip)");
        DesignCloseAll(shell);
        DesignOpenHome(shell, 10);
        await DesignPairAsync(rig, "folder-user-mixed", "A user folder (Couch co-op) with games from four platforms");
        DesignCloseAll(shell);
        DesignOpenHome(shell, 4);
        await DesignPairAsync(rig, "folder-ps1-square-covers", "PlayStation folder: square (1:1) jewel-case covers");
        DesignCloseAll(shell);
        DesignOpenHome(shell, 6);
        await DesignPairAsync(rig, "folder-psp-tall-covers", "PSP folder: tall (0.58) UMD covers");
        DesignCloseAll(shell);
        DesignOpenHome(shell, 7);
        await DesignPairAsync(rig, "folder-3ds-landscape-covers", "Nintendo 3DS folder: landscape (1.13) covers, cartridge-era backdrop");
        DesignCloseAll(shell);
        DesignOpenHome(shell, 2);
        DesignFocusFolder(shell, DesignIndexIn(shell, "Metroid Prime"));
        await DesignPairAsync(rig, "folder-gamecube-game-focused", "GameCube folder, Metroid Prime focused");
    }

    [AvaloniaFact]
    public async Task DesignSnapshot_TitlePagesLaunchAndCompanion()
    {
        if (DesignOutput is null)
            return;
        using var rig = await DesignRigAsync();
        var shell = rig.Shell;

        DesignOpenHome(shell, 1);
        await DesignOpenTitleAsync(shell, "Okami");
        await DesignPairAsync(rig, "title-fanart", "Title page (Okami): fan art on the Stage, title set in type (no logo); Play + Achievements");
        Press(shell, GamepadAction.NavigateRight);
        await DesignPairAsync(rig, "title-achievements-button-focused", "Title page, Achievements button focused");
        Press(shell, GamepadAction.Actions);
        await DesignPairAsync(rig, "title-actions-sheet", "Title page with its Y actions sheet");
        Press(shell, GamepadAction.Cancel);
        Press(shell, GamepadAction.Cancel);
        await DesignOpenTitleAsync(shell, "Shadow of the Colossus");
        await DesignPairAsync(rig, "title-fanart-logo", "Title page (Shadow of the Colossus): fan art with a logo on the Stage");
        Press(shell, GamepadAction.Cancel);
        await DesignOpenTitleAsync(shell, "Ico");
        await DesignPairAsync(rig, "title-cover-fallback", "Title page (Ico): no fan art, so the cover on the platform tint");
        Press(shell, GamepadAction.Cancel);
        await DesignOpenTitleAsync(shell, "Katamari Damacy");
        await DesignPairAsync(rig, "title-no-cover", "Title page for a game with no cover and no art");
        DesignCloseAll(shell);

        DesignOpenHome(shell, 4);
        await DesignOpenTitleAsync(shell, "Final Fantasy VII");
        await DesignPairAsync(rig, "title-multi-disc", "Title page (Final Fantasy VII, 3 discs): Play, Disc, Achievements");
        var discIndex = shell.TitlePage!.Buttons.ToList().FindIndex(button => button.Kind == HandheldTitleButtonKind.Disc);
        Press(shell, GamepadAction.NavigateRight, discIndex);
        Press(shell, GamepadAction.Confirm);
        Assert.True(shell.TitlePage!.IsOptionList);
        await DesignPairAsync(rig, "title-disc-chooser", "Title page, disc chooser card open");
        Press(shell, GamepadAction.Cancel);
        Press(shell, GamepadAction.Cancel);

        await DesignOpenTitleAsync(shell, "Metal Gear Solid");
        Press(shell, GamepadAction.Confirm);
        Assert.True(shell.TitlePage!.IsScreenChooser);
        await DesignPairAsync(rig, "title-screen-chooser", "Launch-screen chooser (system set to Ask): top screen card focused");
        Press(shell, GamepadAction.NavigateRight);
        await DesignPairAsync(rig, "title-screen-chooser-bottom-focused", "Launch-screen chooser, bottom screen card focused");
        Press(shell, GamepadAction.Cancel);
        Press(shell, GamepadAction.Cancel);

        // Launching: held open by a pending launch, then the game runs and the companion is the bottom screen.
        await DesignOpenTitleAsync(shell, "Final Fantasy VII");
        var launch = new TaskCompletionSource<bool>();
        rig.Data.Library.PendingLaunch = launch;
        Press(shell, GamepadAction.Confirm);
        Assert.True(shell.TitlePage!.IsLaunching);
        await DesignPairAsync(rig, "title-launching", "Launching: the cover and progress while the emulator starts");
        launch.SetResult(true);
        await DesignSettleAsync();

        var companion = shell.CompanionScreen!;
        rig.Data.Companion.IsGameRunning = true;
        rig.Data.Companion.Overlay = SecondScreenOverlayKind.Achievements;
        var view = new HandheldCompanionView(false) { DataContext = companion };
        var window = new Window { Content = view, Width = 1240, Height = 1080 };
        window.Show();
        try
        {
            await DesignDumpAsync((window, "handheld-companion-ingame-achievements-bottom",
                "Companion (in game, bottom screen): now playing, achievement ring, recent unlocks, dock"));
            companion.Dispatch(GamepadAction.NextPlatform);
            await DesignDumpAsync((window, "handheld-companion-ingame-photos-bottom", "Companion (in game): Photos tab (screenshots)"));
            companion.Dispatch(GamepadAction.PreviousPlatform);
            companion.Dispatch(GamepadAction.Confirm);
            await DesignDumpAsync((window, "handheld-companion-ingame-badges-bottom", "Companion (in game): the full badge grid"));
            companion.Dispatch(GamepadAction.Cancel);
            companion.Dispatch(GamepadAction.NavigateDown);
            await DesignDumpAsync((window, "handheld-companion-ingame-dock-bottom", "Companion (in game): dock focused"));
            companion.Dispatch(GamepadAction.NavigateUp);

            // The game ends: All apps over the Launcher in its light look, with the Stage beside it.
            rig.Data.Companion.IsGameRunning = false;
            rig.Data.Companion.DrawerTitle = "All apps";
            rig.Data.Companion.Overlay = SecondScreenOverlayKind.Drawer;
            await DesignDumpAsync(
                (window, "handheld-all-apps-bottom", "All apps drawer over the Launcher (companion, light look)"),
                (rig.StageWindow, "handheld-all-apps-top", "Stage while All apps is open"));
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaFact]
    public async Task DesignSnapshot_SearchAddGamesAndCoverSearch()
    {
        if (DesignOutput is null)
            return;
        using var rig = await DesignRigAsync();
        var shell = rig.Shell;
        var covers = Path.Combine(rig.Data.Media.FullName, "covers");
        Directory.CreateDirectory(covers);
        rig.Data.Library.CoverSearchFactory = game => new GamepadCoverSearchViewModel(
            new CoverSearchViewModel(new GameCoverPickerContext(game.Title, game.SystemName, 0.708), new DesignCoverResults(game.Title),
                new DesignCoverDownloads(covers, game.Title, game.SystemId), () => Task.FromResult<string?>(null)), () => { },
            offersLocalFile: false);

        FocusHome(shell, 1);
        RunMenuRow(shell, "Search");
        var search = Assert.IsType<HandheldSearchPanel>(shell.Panel);
        await DesignPairAsync(rig, "search-empty", "Search panel open (on-screen keyboard), nothing typed");
        search.Keyboard!.Text = "mario";
        await search.Pending;
        await DesignPairAsync(rig, "search-typing", "Search panel with \"mario\" typed: matches fan out on the Stage");
        Press(shell, GamepadAction.Search);
        await search.Pending;
        await DesignPairAsync(rig, "search-results-folder", "Search results shown as a folder");
        DesignCloseAll(shell);

        RunMenuRow(shell, "Add games");
        Assert.NotNull(shell.Sheet);
        await DesignPairAsync(rig, "add-games-system-sheet", "Add games: after picking a folder, the which-system sheet");
        Press(shell, GamepadAction.Cancel);

        DesignOpenHome(shell, 2);
        DesignFocusFolder(shell, DesignIndexIn(shell, "Pikmin"));
        Press(shell, GamepadAction.Actions);
        RunSheetRow(shell, "Set cover");
        var panel = Assert.IsType<HandheldCoverSearchPanel>(shell.Panel);
        for (var i = 0; i < 300 && panel.Rows.Count < 8; i++)
            await Task.Delay(10);
        await DesignPairAsync(rig, "cover-search-results", "Set cover: web cover search results, first cover focused and shown large");
        Press(shell, GamepadAction.NavigateDown);
        await DesignPairAsync(rig, "cover-search-second-focused", "Set cover: the next cover focused");
    }

    [AvaloniaFact]
    public async Task DesignSnapshot_SettingsSections()
    {
        if (DesignOutput is null)
            return;
        using var rig = await DesignRigAsync();
        var shell = rig.Shell;

        FocusHome(shell, 9);
        Press(shell, GamepadAction.Confirm);
        var settings = Assert.IsType<HandheldSettingsViewModel>(shell.Settings);
        await DesignPairAsync(rig, "settings-open", "Settings open on its first section");
        foreach (var section in settings.Sections.ToList())
        {
            var slug = new string(section.Title.ToLowerInvariant().Select(c => char.IsLetterOrDigit(c) ? c : '-').ToArray()).Trim('-');
            while (slug.Contains("--", StringComparison.Ordinal))
                slug = slug.Replace("--", "-", StringComparison.Ordinal);
            settings.TapSection(section);
            await DesignSettleAsync();
            if (settings.Projection.IsRailFocused)
            {
                await DesignPairAsync(rig, $"settings-{slug}-rail", $"Settings › {section.Title}: the section list has the pad");
                Press(shell, GamepadAction.NavigateRight);
            }
            await DesignPairAsync(rig, $"settings-{slug}", $"Settings › {section.Title}: first row focused, described on the Stage");
            if (!settings.Projection.IsRailFocused)
                Press(shell, GamepadAction.NavigateLeft);
        }
    }

    [AvaloniaFact]
    public async Task DesignSnapshot_Activity()
    {
        if (DesignOutput is null)
            return;
        using var rig = await DesignRigAsync();
        var shell = rig.Shell;

        // The play log (M46 P4): the week's sessions, the ranking, one game's sessions, and editing a session.
        FocusHome(shell, 14);
        Press(shell, GamepadAction.Confirm);
        var panel = Assert.IsType<HandheldActivityPanel>(shell.Panel);
        await WaitUntil(() => !panel.Activity.IsLoading && panel.Rows.Count > 0);
        await DesignPairAsync(rig, "activity-week", "Activity open: this week's sessions, the focused session's day on the Stage");
        Press(shell, GamepadAction.NavigateDown, 2);
        await DesignPairAsync(rig, "activity-week-third-session", "Activity: a session further down focused");
        Press(shell, GamepadAction.NextPlatform);
        await WaitUntil(() => panel.Activity.IsRanking && !panel.Activity.IsLoading);
        await DesignPairAsync(rig, "activity-ranking", "Activity: the ranking (RB), most played first, the game's stats on the Stage");
        Press(shell, GamepadAction.Confirm);
        await WaitUntil(() => panel.Activity.IsGamePage && !panel.Activity.IsLoading);
        await DesignPairAsync(rig, "activity-game", "Activity: one game's sessions");
        Press(shell, GamepadAction.Actions);
        Assert.True(panel.Activity.IsEditing);
        await DesignPairAsync(rig, "activity-edit-session", "Activity: Y edits a session (Save / Delete / Cancel)");
        Press(shell, GamepadAction.Cancel);
        Press(shell, GamepadAction.Cancel);
        await WaitUntil(() => panel.Activity.IsRanking && !panel.Activity.IsLoading);
        Press(shell, GamepadAction.Cancel);
        Assert.Null(shell.Panel);

        // A game's Y sheet has a Play history row; the Title page shows when it was first played.
        DesignOpenHome(shell, 1);
        await DesignOpenTitleAsync(shell, "Final Fantasy X");
        await DesignPairAsync(rig, "title-play-log-facts", "Title page (Final Fantasy X): first played and per session, from the play log");
    }

    [AvaloniaFact]
    public async Task DesignSnapshot_SwappedScreens()
    {
        if (DesignOutput is null)
            return;
        using var rig = await DesignRigAsync(swapped: true);
        var shell = rig.Shell;

        DesignFocusNamed(shell, "PlayStation 2");
        await DesignPairAsync(rig, "home-platform-folder-focused", "Screens swapped (X): Launcher on the 16:9 top screen (7×3 home), Stage below");
        DesignFocusNamed(shell, "Ico");
        await DesignPairAsync(rig, "home-pinned-game-focused", "Screens swapped: a pinned game focused");
        DesignFocusNamed(shell, "PlayStation 2");
        Press(shell, GamepadAction.Confirm);
        await DesignPairAsync(rig, "folder-platform", "Screens swapped: inside the PS2 folder (7 columns)");
        await DesignOpenTitleAsync(shell, "Okami");
        await DesignPairAsync(rig, "title-fanart", "Screens swapped: Title page");
    }

    [AvaloniaFact]
    public async Task DesignSnapshot_IdleDriftAndEmptyLibrary()
    {
        if (DesignOutput is null)
            return;
        using (var rig = await DesignRigAsync())
        {
            var shell = rig.Shell;
            shell.StartStatusUpdates();
            await shell.OnIdleAsync();
            await DesignPairAsync(rig, "idle-drift-fanart", "Idle drift (15 s untouched): the newest recent game as the Stage hero, with fan art");
            await shell.OnIdleAsync();
            await DesignPairAsync(rig, "idle-drift-cover", "Idle drift, next recent game without fan art: cover on the tint");
            shell.StopStatusUpdates();
        }

        using (var empty = await DesignRigAsync(empty: true))
        {
            await DesignPairAsync(empty, "empty-library", "First run, empty library: the Settings tile and the Add games slot (focused)");
        }
    }
}
