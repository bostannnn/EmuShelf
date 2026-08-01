using CommunityToolkit.Mvvm.ComponentModel;
using EmuShelf.App.Services;
using EmuShelf.Core.TexturePacks;

namespace EmuShelf.App.ViewModels;

/// <summary>One discovered pack as the Settings inventory list shows it.</summary>
public sealed class TexturePackEntryViewModel
{
    public TexturePackEntryViewModel(
        TexturePackClassification classification,
        IReadOnlyDictionary<long, string> gameTitles)
    {
        Classification = classification;
        EmulatorName = TexturePackProviderRegistry.DescribeEmulator(classification.EmulatorId);
        MatchedGames = classification.MatchedGameIds.Count == 0
            ? string.Empty
            : string.Join(", ", classification.MatchedGameIds
                .Select(id => gameTitles.TryGetValue(id, out var title) ? title : $"Game {id}"));
    }

    public TexturePackClassification Classification { get; }

    public string PackKey => Classification.PackKey;

    public string SourcePath => Classification.SourcePath;

    public string EmulatorName { get; }

    public string MatchedGames { get; }

    public bool HasMatchedGames => MatchedGames.Length > 0;

    public TexturePackEntryStatus Status => Classification.Status;

    /// <summary>
    /// Status wording written for someone deciding what to do next. "No library match" in
    /// particular must not read as a fault: the pack may be perfectly good and simply for a game
    /// that has not been imported.
    /// </summary>
    public string StatusText => Classification.Status switch
    {
        TexturePackEntryStatus.Matched => "Matched",
        TexturePackEntryStatus.NoLibraryMatch => "No game in your library",
        TexturePackEntryStatus.SharedPack => "Shared pack",
        TexturePackEntryStatus.EmptyOrDumpsOnly => "Empty or dumps only",
        TexturePackEntryStatus.UnrecognizedLayout => "Unrecognized layout",
        TexturePackEntryStatus.FolderUnavailable => "Folder unavailable",
        TexturePackEntryStatus.IdentifierPending => "Identification pending",
        _ => "Unknown",
    };

    public string StatusTooltip => Classification.Diagnostic ?? Classification.Status switch
    {
        TexturePackEntryStatus.Matched => "This pack is installed and matches a game in your library.",
        TexturePackEntryStatus.NoLibraryMatch =>
            "No imported game uses this identifier. The pack isn't broken — the game may simply not be in your library.",
        TexturePackEntryStatus.SharedPack =>
            "This pack applies to every game rather than one identifier, so it isn't matched to a single title.",
        TexturePackEntryStatus.IdentifierPending =>
            "Your library hasn't extracted the identifiers this pack is keyed on yet.",
        _ => string.Empty,
    };
}

/// <summary>
/// One platform's detected texture root, override, and loading state in Settings. The row exposes
/// no operation that changes a pack — only where EmuShelf looked and what it found.
/// </summary>
public partial class TexturePackRowViewModel : ObservableObject
{
    public TexturePackRowViewModel(TexturePackPlatformState state, string overridePlaceholder)
    {
        ArgumentNullException.ThrowIfNull(state);
        SystemId = state.SystemId;
        DisplayName = state.DisplayName;
        OverridePlaceholder = overridePlaceholder;
        Apply(state);
    }

    public string SystemId { get; }

    public string FolderFieldId => $"textures.{SystemId}.folder";

    public string DetectedFieldId => $"textures.{SystemId}.detected";

    public string DisplayName { get; }

    public string OverridePlaceholder { get; }

    [ObservableProperty]
    public partial string? DetectedRoot { get; set; }

    [ObservableProperty]
    public partial string DirectoryOverride { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string StatusText { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string LoadingText { get; set; } = string.Empty;

    [ObservableProperty]
    public partial bool CanOpenFolder { get; set; }

    public void Apply(TexturePackPlatformState state)
    {
        DetectedRoot = state.DetectedRoot;
        CanOpenFolder = state.DetectedRoot is not null;
        if (state.IsOverridden && state.DetectedRoot is not null)
            DirectoryOverride = state.DetectedRoot;

        StatusText = Describe(state);
        LoadingText = state.Loading switch
        {
            TexturePackLoadingStatus.Enabled => "Replacement loading is on.",
            TexturePackLoadingStatus.Disabled =>
                "Replacement loading is off in this emulator — installed packs won't be used.",
            _ => "Loading status unknown.",
        };
    }

    private static string Describe(TexturePackPlatformState state)
    {
        if (state.DetectedRoot is null)
            return state.Diagnostic ?? "No texture folder detected.";

        var status = state.RootStatus switch
        {
            TexturePackRootStatus.Ready => "Folder read.",
            TexturePackRootStatus.Missing => "Folder not found.",
            TexturePackRootStatus.Unreadable => "Folder could not be read.",
            _ => "Not scanned yet.",
        };

        // A stale result is the disconnected-drive case: the cached inventory is still shown, and
        // saying so is the difference between "your packs vanished" and "plug the drive back in".
        return state.IsStale
            ? $"{status} Showing the last successful scan."
            : status;
    }
}
