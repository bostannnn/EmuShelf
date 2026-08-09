using Avalonia.Headless.XUnit;
using EmuShelf.App.ViewModels;
using EmuShelf.Core.Importing;
using EmuShelf.Core.Launching;
using EmuShelf.Core.Library;
using EmuShelf.Core.Systems;
using EmuShelf.Infrastructure.Importing;
using EmuShelf.Infrastructure.Library;
using EmuShelf.Infrastructure.Persistence;
using EmuShelf.Infrastructure.Storage;
using EmuShelf.Integrations.Importing;
using EmuShelf.Integrations.Systems;

namespace EmuShelf.App.Tests;

/// <summary>
/// The library toast is the only surface for operational feedback, so what it says and how long
/// it earns is behaviour worth pinning: results and failures must be distinguishable, and a
/// failure must not be discarded on the same short timer a routine result gets.
/// </summary>
public class StatusToastTests : IDisposable
{
    private readonly string _baseDirectory =
        Path.Combine(Path.GetTempPath(), "EmuShelfToast", Guid.NewGuid().ToString("N"));
    private readonly GameLibrary _library;
    private readonly LibraryDatabase _database;
    private readonly FakeDialogService _dialogs = new();

    public StatusToastTests()
    {
        var appPaths = new AppPaths(_baseDirectory);
        appPaths.EnsureDirectoriesExist();
        _database = new LibraryDatabase(appPaths);
        _database.Initialize();
        _library = new GameLibrary(_database, new RelativePathResolver(appPaths));
    }

    private MainViewModel CreateViewModel()
    {
        IGameImportRules rules = new FileImportRules();
        return new MainViewModel(
            _library,
            new FolderScanner(rules),
            rules,
            new FileAvailabilityChecker(),
            _dialogs,
            KnownSystems.All);
    }

    [AvaloniaFact]
    public void AnEmptyMessageMeansNoToast()
    {
        var viewModel = CreateViewModel();

        Assert.False(viewModel.HasStatusMessage);
        Assert.False(viewModel.IsStatusError);
    }

    [AvaloniaFact]
    public void DismissingClearsTheToast()
    {
        var viewModel = CreateViewModel();
        viewModel.StatusText = "Something happened";
        Assert.True(viewModel.HasStatusMessage);

        viewModel.ClearStatusCommand.Execute(null);

        Assert.False(viewModel.HasStatusMessage);
    }

    [AvaloniaFact]
    public async Task ARejectedRenameIsReportedAsAnError()
    {
        var viewModel = CreateViewModel();
        var game = await AddGameAsync(viewModel, "Alpha");
        game.DraftTitle = "   ";

        await viewModel.SaveGameTitleCommand.ExecuteAsync(game);

        Assert.Equal("A game title cannot be empty.", viewModel.StatusText);
        Assert.True(viewModel.IsStatusError);
    }

    [AvaloniaFact]
    public async Task ASuccessfulRenameIsNotReportedAsAnError()
    {
        var viewModel = CreateViewModel();
        var game = await AddGameAsync(viewModel, "Alpha");
        game.DraftTitle = "Alpha Prime";

        await viewModel.SaveGameTitleCommand.ExecuteAsync(game);

        Assert.Equal("Renamed game to Alpha Prime", viewModel.StatusText);
        Assert.False(viewModel.IsStatusError);
    }

    [AvaloniaFact]
    public async Task LaunchingAGameWhoseFileIsGoneReportsAnError()
    {
        var viewModel = CreateViewModel();
        var game = await AddGameAsync(viewModel, "Gone");
        File.Delete(game.LaunchModel.Path);
        await viewModel.RefreshAvailabilityAsync();

        var unavailable = viewModel.Games.Single(candidate => candidate.Title == "Gone");
        Assert.False(unavailable.IsAvailable);

        await viewModel.LaunchGameCommand.ExecuteAsync(unavailable);

        Assert.True(viewModel.HasStatusMessage);
        Assert.True(viewModel.IsStatusError);
    }

    /// <summary>
    /// A failure has to outlive a routine result — the toast is the only place it is reported,
    /// so the two must not share one lifetime.
    /// </summary>
    [AvaloniaFact]
    public async Task AnErrorOutlivesAResult()
    {
        var viewModel = CreateViewModel();
        var game = await AddGameAsync(viewModel, "Alpha");

        game.DraftTitle = "Alpha Prime";
        await viewModel.SaveGameTitleCommand.ExecuteAsync(game);
        var resultLifetime = viewModel.StatusDismissDelay;

        game.DraftTitle = string.Empty;
        await viewModel.SaveGameTitleCommand.ExecuteAsync(game);
        var errorLifetime = viewModel.StatusDismissDelay;

        Assert.True(resultLifetime > TimeSpan.Zero);
        Assert.True(errorLifetime > resultLifetime);
    }

    /// <summary>
    /// Progress commentary gets no countdown: a scan that pauses on a slow folder must not have
    /// its own running status wiped out from under it.
    /// </summary>
    [AvaloniaFact]
    public void ProgressCommentaryNeverExpiresOnItsOwn()
    {
        var viewModel = CreateViewModel();

        viewModel.StatusSeverity = StatusSeverity.Progress;
        viewModel.StatusText = "Scanning PlayStation… 41 found";

        Assert.Equal(TimeSpan.Zero, viewModel.StatusDismissDelay);
    }

    /// <summary>
    /// A launch/exit save sync raises the large centered Gamepad panel from the same StatusText the
    /// corner toast reads, so while that panel is up the corner toast must stay silent — otherwise
    /// the couch player sees the identical sync line twice, once big and once in the corner.
    /// </summary>
    [AvaloniaFact]
    public void TheGamepadCornerToastIsSuppressedWhileTheLaunchSyncPanelIsShowing()
    {
        var viewModel = CreateViewModel();
        var changes = 0;
        viewModel.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(MainViewModel.ShowGamepadStatusToast))
                changes++;
        };

        viewModel.StatusText = "Syncing saves before launching Alpha…";
        Assert.True(viewModel.HasStatusMessage);
        Assert.True(viewModel.ShowGamepadStatusToast);

        viewModel.IsSyncingSavesForLaunch = true;
        Assert.True(viewModel.HasStatusMessage);
        Assert.False(viewModel.ShowGamepadStatusToast);

        viewModel.IsSyncingSavesForLaunch = false;
        Assert.True(viewModel.ShowGamepadStatusToast);

        Assert.True(changes >= 3);
    }

    private async Task<GameViewModel> AddGameAsync(MainViewModel viewModel, string title)
    {
        var folder = Path.Combine(_baseDirectory, "roms");
        Directory.CreateDirectory(folder);
        var path = Path.Combine(folder, $"{title}.cue");
        File.WriteAllText(path, "x");
        _library.AddGames([new Game
        {
            SystemId = "playstation",
            Path = path,
            Title = title,
            DateAdded = DateTimeOffset.UtcNow,
        }]);

        await viewModel.ReloadGamesAsync();
        return viewModel.Games.Single(candidate => candidate.Title == title);
    }

    public void Dispose()
    {
        if (!Directory.Exists(_baseDirectory))
            return;

        for (var attempt = 0; ; attempt++)
        {
            try
            {
                Directory.Delete(_baseDirectory, recursive: true);
                return;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException && attempt < 40)
            {
                Thread.Sleep(50);
            }
        }
    }
}
