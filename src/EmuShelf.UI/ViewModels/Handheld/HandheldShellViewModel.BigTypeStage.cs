using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Media;
using Avalonia.Media.Immutable;

namespace EmuShelf.App.ViewModels;

/// <summary>
/// What the Stage says in the Big Type look (M47 B4): the label top left, the name fitted across the screen, the line under it
/// and the second line, as the mock's <c>describe</c> writes them. They are set for every Stage in either look (today's look
/// never draws them), from what the shell already holds; the emulator's name and a folder's last played game arrive a moment
/// later, read once and kept.
/// </summary>
public partial class HandheldShellViewModel
{
    /// <summary>The dark ink of the Stage in the Big Type look: <see cref="LauncherGround"/> mixed toward near-black.</summary>
    [CommunityToolkit.Mvvm.ComponentModel.ObservableProperty]
    [CommunityToolkit.Mvvm.ComponentModel.NotifyPropertyChangedFor(nameof(StageInk), nameof(StageBatteryInk), nameof(StageFactsInk))]
    public partial Color StageDeep { get; private set; } = HandheldTokens.StageDeep(HandheldTokens.DefaultGround);

    /// <summary>
    /// The Avalonia Stage's ink in the Big Type look: it draws the pictured fallback, never a shader, so its text takes the
    /// fallback's answer, the dark ink (M47 B5; <c>BackdropLegibility.Fallback</c>). The native Stage tints its pictures itself.
    /// </summary>
    public IBrush StageInk => new ImmutableSolidColorBrush(StageDeep);

    /// <summary>The battery's ink: the Stage's at the battery's opacity, carried in the brush (B4: Android cuts text with its own opacity).</summary>
    public IBrush StageBatteryInk => new ImmutableSolidColorBrush(StageDeep, HandheldTokens.StageBatteryOpacity);

    /// <summary>The second line's ink: the Stage's at its opacity.</summary>
    public IBrush StageFactsInk => new ImmutableSolidColorBrush(StageDeep, HandheldTokens.StageFactsOpacity);

    partial void OnLauncherGroundChanged(Color value) => StageDeep = HandheldTokens.StageDeep(value);

    // Each system's emulator by the name Settings shows, read once from the Title page's details (null: none could be named).
    private readonly Dictionary<string, string?> _emulatorNames = new(StringComparer.Ordinal);
    private readonly HashSet<string> _emulatorLoading = new(StringComparer.Ordinal);

    // The recently played games' titles and systems, newest first: a folder's "Last played" (Big Type only).
    private IReadOnlyList<HandheldGameEntry> _recentGames = [];
    private int _recentGamesGeneration;
    private HandheldFolderSource? _stageFolder;

    /// <summary>The defaults for any Stage: no label, the title as the name, the subtitle as the line.</summary>
    private void ApplyBigTypeText(HandheldStageKind kind, string title, string subtitle)
    {
        _stageFolder = null;
        BtIsMissing = false;
        BtFacts = string.Empty;
        var sorting = OpenFolder?.IsSortFocused == true;
        BtLabel = kind switch
        {
            HandheldStageKind.SettingsTile or HandheldStageKind.ActivityTile => "System",
            HandheldStageKind.App or HandheldStageKind.AllApps or HandheldStageKind.Menu => "Dock",
            HandheldStageKind.EmptySlot => IsLibraryEmpty ? "Welcome" : "Home",
            HandheldStageKind.Header => OpenFolder?.Name ?? string.Empty,
            _ => string.Empty,
        };
        BtName = kind switch
        {
            HandheldStageKind.EmptySlot => IsLibraryEmpty ? "No games yet" : "Empty",
            HandheldStageKind.Header => sorting ? "Sort" : "Back",
            _ => title,
        };
        BtLine = kind switch
        {
            HandheldStageKind.Header when sorting && OpenFolder is { } folder => $"{folder.SortLabel} · A changes the order",
            HandheldStageKind.Header => "Back to home",
            _ => subtitle,
        };
    }

    /// <summary>A folder: its console's full name over its short name, or its kind over its own name; its count; its last played game.</summary>
    private void ApplyBigTypeFolderText(HandheldFolderSource source, string subtitle)
    {
        _stageFolder = source;
        BtLabel = source.Kind switch
        {
            HandheldFolderKind.Platform when source.Platform is { } platform => platform.Name,
            HandheldFolderKind.Recent => "Last played",
            HandheldFolderKind.User => "Your folder",
            HandheldFolderKind.Search => "Search",
            _ => string.Empty,
        };
        BtName = source.Platform is { ShortName.Length: > 0 } shortNamed ? shortNamed.ShortName : source.Name;
        var count = HandheldText.GameCount(source.GameCount);
        BtLine = subtitle.StartsWith("New ·", StringComparison.Ordinal) ? $"New · {count}"
            : subtitle == "Loading…" ? subtitle
            : count;
        BtFacts = FolderFacts(source);
    }

    private string FolderFacts(HandheldFolderSource source)
    {
        HandheldGameEntry? last = source.Kind switch
        {
            HandheldFolderKind.Platform when source.Platform is { } platform =>
                _recentGames.FirstOrDefault(game => game.SystemId == platform.SystemId),
            HandheldFolderKind.Recent => _recentGames.FirstOrDefault(),
            _ when source.GameIds is { } ids => _recentGames.FirstOrDefault(game => ids.Contains(game.Id)),
            _ => null,
        };
        return last is null ? string.Empty : $"Last played: {last.Title}";
    }

    /// <summary>A game in the browser shows its console, title, play time and achievement progress.</summary>
    private void ApplyBigTypeGameText(HandheldGameEntry game)
    {
        BtLabel = game.SystemName;
        BtName = game.Title;
        BtIsMissing = !game.IsAvailable;
        // M47 B12: in the model view the line is the name and the second line the facts (HandheldShellViewModel.ModelView.cs).
        _stageGame = game;
        _modelTexts = false;
        if (ModelViewDue)
        {
            ApplyModelTexts(game);
            return;
        }
        if (!game.IsAvailable)
        {
            BtLine = "File not found";
            BtFacts = string.Empty;
            return;
        }
        var played = game.Playtime.TotalMinutes >= 1 ? $"{HandheldText.Duration(game.Playtime)} played"
            : game.PlayCount == 0 ? "Not played yet" : "Played";
        BtLine = string.IsNullOrEmpty(game.AchievementText) ? played : $"{played} · {game.AchievementText} achievements";
        BtFacts = string.Empty;
    }

    private string GameFacts(HandheldGameEntry game)
    {
        var parts = new List<string>(3);
        if (_emulatorNames.TryGetValue(game.SystemId, out var emulator) && !string.IsNullOrEmpty(emulator))
            parts.Add(emulator);
        if (game.PlayCount > 0 && game.LastPlayedAt is not null)
        {
            var when = HandheldText.LastPlayed(game.LastPlayedAt, _clock.GetLocalNow());
            // "Today", "Yesterday" and "3 days ago" read in a sentence; a date keeps its capital.
            if (when.Length > 0 && !char.IsDigit(when[0]))
                when = char.ToLowerInvariant(when[0]) + when[1..];
            parts.Add($"last played {when}");
        }
        if (game.ViewModel is { } viewModel && viewModel.Discs.Count > 1)
            parts.Add($"disc {viewModel.SelectedDiscNumber} of {viewModel.Discs.Count}");
        var facts = string.Join(" · ", parts);
        return facts.Length == 0 ? facts : char.ToUpperInvariant(facts[0]) + facts[1..];
    }

    // The emulator's name comes with the Title page's details; read once per system and kept for this Handheld session.
    private async Task LoadEmulatorNameAsync(HandheldGameEntry game)
    {
        if (!_emulatorLoading.Add(game.SystemId))
            return;
        string? name;
        try
        {
            name = (await _library.GetTitleDetailsAsync(game)).EmulatorName;
        }
        catch (Exception)
        {
            name = null;
        }
        finally
        {
            _emulatorLoading.Remove(game.SystemId);
        }
        if (_disposed)
            return;
        _emulatorNames[game.SystemId] = name;
        if (Sheet?.GameDetails is { } details && details.SystemId == game.SystemId)
        {
            Sheet.Items[0].Note = GameFacts(details);
            RefreshListStage();
        }
        if (_modelTexts && Stage.Kind == HandheldStageKind.Game && _stageGame is { IsAvailable: true } current &&
            current.Id == _stageGameId && current.SystemId == game.SystemId)
            BtFacts = ModelFacts(current);
    }

    /// <summary>Settings may have changed a system's emulator: its name is read again next time.</summary>
    private void ForgetEmulatorNames() => _emulatorNames.Clear();

    /// <summary>The recently played games' titles, for a folder's "Last played" (read only in the Big Type look).</summary>
    private async Task LoadRecentGamesAsync()
    {
        if (!HandheldTokens.IsBigType)
            return;
        var generation = ++_recentGamesGeneration;
        IReadOnlyList<HandheldGameEntry> games;
        try
        {
            games = await _library.GetGamesByIdAsync(_recentIds);
        }
        catch (Exception)
        {
            return;
        }
        if (_disposed || generation != _recentGamesGeneration)
        {
            _library.ReleaseGames(games);
            return;
        }
        // Only the titles and systems are kept; the entries' view models go back at once.
        var order = _recentIds.Select((id, index) => (id, index)).GroupBy(pair => pair.id).ToDictionary(group => group.Key, group => group.First().index);
        _recentGames = games.Select(game => game with { ViewModel = null }).OrderBy(game => order.GetValueOrDefault(game.Id, int.MaxValue)).ToList();
        _library.ReleaseGames(games);
        if (_stageFolder is { } folder && Stage.Kind == HandheldStageKind.Folder)
            BtFacts = FolderFacts(folder);
    }
}
