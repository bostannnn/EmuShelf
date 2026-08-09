using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using EmuShelf.App.Services;
using EmuShelf.Core.Diagnostics;
using EmuShelf.Core.SaveSync;

namespace EmuShelf.App.ViewModels;

/// <summary>
/// One supported save-sync platform row in Settings. Every platform renders from this same view
/// model over one template, so adding a provider adds a row without touching the view.
/// </summary>
public partial class CloudSavePlatformRowViewModel : ViewModelBase
{
    private readonly CloudSaveSyncSettingsContext _cloudSaves;
    private readonly IDialogService _dialogs;
    private readonly IAppLogger _logger;
    private readonly Func<string, SaveSyncDirection, Task> _force;
    private bool _isInitializing = true;

    public CloudSavePlatformRowViewModel(
        CloudSaveSyncPlatformContext platform,
        CloudSaveSyncSettingsContext cloudSaves,
        IDialogService dialogs,
        IAppLogger logger,
        Func<string, SaveSyncDirection, Task> force)
    {
        _cloudSaves = cloudSaves;
        _dialogs = dialogs;
        _logger = logger;
        _force = force;
        SystemId = platform.SystemId;
        DisplayName = platform.DisplayName;
        SaveShapeDescription = platform.SaveShapeDescription;
        OverridePlaceholder = platform.OverridePlaceholder;
        OverrideDirectory = platform.Override ?? string.Empty;
        StateOverrideDirectory = platform.StateOverride ?? string.Empty;
        LastResultText = DescribeLastResult(platform);
        LastNoticeText = platform.LastNotice;
        SupportsSaveStates = platform.SupportsSaveStates;
        SaveStatesLabel = platform.SaveStatesLabel ?? "Automatically sync save states";
        SyncSaveStates = platform.SyncSaveStates;
        _isInitializing = false;
    }

    /// <summary>The stable system id this row configures.</summary>
    public string SystemId { get; }

    public string FolderFieldId => $"saves.{SystemId}.folder";

    public string SaveStatesFieldId => $"saves.{SystemId}.states";

    public string StateFolderFieldId => $"saves.{SystemId}.states-folder";

    public string ReplaceCloudFieldId => $"saves.{SystemId}.replace-cloud";

    public string ReplaceLocalFieldId => $"saves.{SystemId}.replace-local";

    /// <summary>The platform name, e.g. <c>PlayStation 2</c>.</summary>
    public string DisplayName { get; }

    /// <summary>One short line describing what this platform syncs.</summary>
    public string SaveShapeDescription { get; }

    /// <summary>Placeholder shown in the override path box.</summary>
    public string OverridePlaceholder { get; }

    public bool SupportsSaveStates { get; }

    /// <summary>The save-states toggle label; platforms may override it (the GameCube row names GC + Wii).</summary>
    public string SaveStatesLabel { get; }

    public bool HasOptionalContent => SupportsSaveStates;

    [ObservableProperty]
    public partial bool SyncSaveStates { get; set; }

    [ObservableProperty]
    public partial string OverrideDirectory { get; set; } = string.Empty;

    /// <summary>An explicit save-state folder, mirroring <see cref="OverrideDirectory"/> for states.</summary>
    [ObservableProperty]
    public partial string StateOverrideDirectory { get; set; } = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasDetectedDirectory))]
    public partial string? DetectedDirectory { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasCompatibilityWarning))]
    public partial string? CompatibilityWarning { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasOptionalContentSummary))]
    public partial string? OptionalContentSummary { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasDetectionError))]
    [NotifyPropertyChangedFor(nameof(CanReplace))]
    public partial string? DetectionErrorText { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasLastResult))]
    public partial string? LastResultText { get; set; }

    /// <summary>
    /// What the last successful pass deliberately left alone, and why. A sync that succeeds without
    /// moving a save the user expected is the case that otherwise looks like a silent failure.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasLastNotice))]
    public partial string? LastNoticeText { get; set; }

    /// <summary>Whether sync is connected, which gates the destructive replace actions.</summary>
    [ObservableProperty]
    public partial bool IsCloudConnected { get; set; }

    /// <summary>Whether a cloud operation is running, which disables this row's controls.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsIdle))]
    [NotifyPropertyChangedFor(nameof(CanReplace))]
    public partial bool IsCloudBusy { get; set; }

    public bool IsIdle => !IsCloudBusy;

    public bool CanReplace => IsIdle && !HasDetectionError;

    public bool HasDetectedDirectory => !string.IsNullOrWhiteSpace(DetectedDirectory);

    public bool HasCompatibilityWarning => !string.IsNullOrWhiteSpace(CompatibilityWarning);

    public bool HasOptionalContentSummary => !string.IsNullOrWhiteSpace(OptionalContentSummary);

    public bool HasDetectionError => !string.IsNullOrWhiteSpace(DetectionErrorText);

    public bool HasLastResult => !string.IsNullOrWhiteSpace(LastResultText);

    public bool HasLastNotice => !string.IsNullOrWhiteSpace(LastNoticeText);

    /// <summary>The override as it should be persisted: trimmed, or null when empty.</summary>
    public string? NormalizedOverride =>
        string.IsNullOrWhiteSpace(OverrideDirectory) ? null : OverrideDirectory.Trim();

    /// <summary>The save-state override as it should be persisted: trimmed, or null when empty.</summary>
    public string? NormalizedStateOverride =>
        string.IsNullOrWhiteSpace(StateOverrideDirectory) ? null : StateOverrideDirectory.Trim();

    /// <summary>
    /// Applies a freshly read platform snapshot after a sync. Only the recorded result is taken:
    /// the override box is left alone because the user may be part-way through editing it.
    /// </summary>
    public void ApplyResult(CloudSaveSyncPlatformContext platform)
    {
        LastResultText = DescribeLastResult(platform);
        LastNoticeText = platform.LastNotice;
    }

    /// <summary>Re-reads the concrete directory this platform resolves to on this machine.</summary>
    public async Task RefreshDetectedDirectoryAsync()
    {
        try
        {
            if (_cloudSaves.GetDetectionAsync is { } detect)
            {
                var detection = await detect(SystemId, CancellationToken.None);
                DetectedDirectory = detection?.DisplayLocation ?? detection?.Directory;
                CompatibilityWarning = detection?.Warning;
                OptionalContentSummary = DescribeOptionalContent(detection);
            }
            else
            {
                DetectedDirectory = await _cloudSaves.GetDetectedPathAsync(SystemId, CancellationToken.None);
                CompatibilityWarning = null;
                OptionalContentSummary = null;
            }

            DetectionErrorText = null;
        }
        catch (Exception ex)
        {
            _logger.Error($"Could not detect the save folder for {DisplayName}.", ex);
            DetectedDirectory = null;
            CompatibilityWarning = null;
            OptionalContentSummary = null;
            DetectionErrorText = $"Cannot sync: {ex.Message}";
        }
    }

    [RelayCommand]
    private async Task PickDirectoryAsync()
    {
        var picked = await _dialogs.PickFolderAsync();
        if (string.IsNullOrWhiteSpace(picked))
            return;

        OverrideDirectory = picked;
        _cloudSaves.UpdateOverride(SystemId, picked);
        DetectedDirectory = null;
        CompatibilityWarning = null;
        DetectionErrorText = null;
        await RefreshDetectedDirectoryAsync();
    }

    [RelayCommand]
    private async Task PickStateDirectoryAsync()
    {
        var picked = await _dialogs.PickFolderAsync();
        if (string.IsNullOrWhiteSpace(picked))
            return;

        StateOverrideDirectory = picked;
        _cloudSaves.UpdateStateOverride?.Invoke(SystemId, picked);
        await RefreshDetectedDirectoryAsync();
    }

    [RelayCommand]
    private Task ReplaceCloudAsync() => _force(SystemId, SaveSyncDirection.Upload);

    [RelayCommand]
    private Task ReplaceLocalAsync() => _force(SystemId, SaveSyncDirection.Download);

    partial void OnSyncSaveStatesChanged(bool value) => PersistOptionalContent();

    private static string? DescribeOptionalContent(SaveProviderDetection? detection)
    {
        if (detection?.OptionalContent is not { Count: > 0 } locations)
            return detection?.OptionalContentSummary;

        return string.Join(Environment.NewLine, locations.Select(location =>
        {
            var path = string.IsNullOrWhiteSpace(location.Directory) ? "Unavailable" : location.Directory;
            var fileCount = location.TotalFileCount > location.EligibleFileCount
                ? $"{location.EligibleFileCount} of {location.TotalFileCount} file(s) selected"
                : $"{location.EligibleFileCount} eligible file(s)";
            var details = $"{fileCount}, {FormatBytes(location.EligibleBytes)}";
            if (!string.IsNullOrWhiteSpace(location.Compatibility))
                details += $", {location.Compatibility}";
            if (!string.IsNullOrWhiteSpace(location.Warning))
                details += $" — {location.Warning}";
            return $"{location.Kind}: {path} · {details}";
        }));

        static string FormatBytes(long bytes)
        {
            string[] suffixes = ["B", "KB", "MB", "GB"];
            var value = (double)bytes;
            var suffix = 0;
            while (value >= 1024 && suffix < suffixes.Length - 1)
            {
                value /= 1024;
                suffix++;
            }
            return $"{value:0.#} {suffixes[suffix]}";
        }
    }

    private void PersistOptionalContent()
    {
        if (!_isInitializing)
            _cloudSaves.UpdateOptionalContent?.Invoke(SystemId, SyncSaveStates);
    }

    private static string? DescribeLastResult(CloudSaveSyncPlatformContext platform)
    {
        if (!string.IsNullOrWhiteSpace(platform.LastError))
            return $"Last attempt failed: {platform.LastError}";
        return platform.LastSuccessUtc is { } success
            ? $"Last synced {success.ToLocalTime():g}"
            : null;
    }
}
