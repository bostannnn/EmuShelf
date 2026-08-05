using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using EmuShelf.Core.Diagnostics;
using EmuShelf.Core.Settings;
using EmuShelf.Core.Updates;

namespace EmuShelf.App.Services;

/// <summary>
/// Drives the in-app update experience: the throttled launch check, the notification banner, and the
/// download-verify-apply-relaunch flow. Bound directly by the main window (as <c>Updates</c>) and
/// reused by Settings so Desktop and Gamepad share one code path. All property changes happen on the
/// thread that invokes the coordinator — the launch check and the commands both run on the UI thread.
/// </summary>
public partial class AppUpdateCoordinator : ObservableObject
{
    // Automatic checks run at most this often; a manual check ignores it.
    private static readonly TimeSpan AutoCheckInterval = TimeSpan.FromHours(20);

    private readonly IUpdateService _updates;
    private readonly IUpdateApplier _applier;
    private readonly ISettingsService _settingsService;
    private readonly IAppLogger _logger;
    private readonly Action _requestExit;
    private AppSettings _settings;
    private UpdateCheckResult.UpdateAvailable? _available;

    public AppUpdateCoordinator(
        IUpdateService updates,
        IUpdateApplier applier,
        ISettingsService settingsService,
        AppSettings settings,
        IAppLogger logger,
        Action requestExit)
    {
        _updates = updates;
        _applier = applier;
        _settingsService = settingsService;
        _settings = settings;
        _logger = logger;
        _requestExit = requestExit;
    }

    /// <summary>Whether the notification banner is showing an available update.</summary>
    [ObservableProperty]
    public partial bool IsBannerVisible { get; set; }

    /// <summary>Headline for the banner and the Settings row, e.g. "EmuShelf 1.2.3 is available".</summary>
    [ObservableProperty]
    public partial string Headline { get; set; } = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsIdle))]
    public partial bool IsBusy { get; set; }

    /// <summary>True before the download reports a percentage, so the bar can spin meanwhile.</summary>
    [ObservableProperty]
    public partial bool IsProgressIndeterminate { get; set; }

    [ObservableProperty]
    public partial int DownloadPercent { get; set; }

    [ObservableProperty]
    public partial string StatusText { get; set; } = string.Empty;

    [ObservableProperty]
    public partial bool HasError { get; set; }

    /// <summary>The version currently being offered, or empty when none.</summary>
    public string AvailableVersion => _available?.Version.ToString() ?? string.Empty;

    /// <summary>Whether a verified-or-not update is available to install right now.</summary>
    public bool HasAvailableUpdate => _available is not null;

    /// <summary>Not busy applying an update — used to gate the Settings install action.</summary>
    public bool IsIdle => !IsBusy;

    /// <summary>Runs the automatic launch check, honouring the opt-out and the once-a-day throttle.</summary>
    public async Task CheckOnLaunchAsync(CancellationToken cancellationToken = default)
    {
        if (!_settings.Updates.AutomaticallyCheck)
            return;
        if (_settings.Updates.LastCheckUtc is { } last &&
            DateTimeOffset.UtcNow - last < AutoCheckInterval)
        {
            return;
        }

        await CheckAsync(manual: false, cancellationToken).ConfigureAwait(true);
    }

    /// <summary>Checks now on the user's request and returns a short line describing the outcome.</summary>
    public Task<string> CheckManuallyAsync(CancellationToken cancellationToken = default) =>
        CheckAsync(manual: true, cancellationToken);

    private async Task<string> CheckAsync(bool manual, CancellationToken cancellationToken)
    {
        UpdateCheckResult result;
        try
        {
            result = await _updates.CheckAsync(cancellationToken).ConfigureAwait(true);
        }
        catch (OperationCanceledException)
        {
            return string.Empty;
        }
        catch (Exception ex)
        {
            _logger.Warning($"Update check failed: {ex.Message}");
            return manual ? "Couldn't check for updates. Check your connection." : string.Empty;
        }

        // Only a check that actually reached GitHub counts against the once-a-day throttle, so an
        // offline launch retries next time rather than staying silent for the whole interval.
        if (result is not UpdateCheckResult.CheckFailed)
            RecordCheckTime();

        switch (result)
        {
            case UpdateCheckResult.UpdateAvailable available:
                var skipped = string.Equals(
                    _settings.Updates.SkippedVersion,
                    available.Version.ToString(),
                    StringComparison.Ordinal);
                _available = available;
                OnPropertyChanged(nameof(AvailableVersion));
                OnPropertyChanged(nameof(HasAvailableUpdate));
                // An auto check honours a skipped version silently; a manual check always shows it.
                if (!manual && skipped)
                    return string.Empty;
                Headline = $"EmuShelf {available.Version} is available";
                HasError = false;
                StatusText = string.Empty;
                IsBannerVisible = true;
                return $"EmuShelf {available.Version} is available.";

            case UpdateCheckResult.UpToDate upToDate:
                _available = null;
                OnPropertyChanged(nameof(AvailableVersion));
                OnPropertyChanged(nameof(HasAvailableUpdate));
                IsBannerVisible = false;
                return $"You're on the latest version ({upToDate.Current}).";

            case UpdateCheckResult.CheckFailed failed:
                return manual ? failed.Reason : string.Empty;

            default:
                return string.Empty;
        }
    }

    /// <summary>Downloads, verifies, applies the pending update, then relaunches.</summary>
    public Task InstallAsync() => UpdateNowAsync();

    [RelayCommand]
    private async Task UpdateNowAsync()
    {
        if (_available is null || IsBusy)
            return;

        if (!_applier.CanApply(out var reason))
        {
            HasError = true;
            StatusText = reason ?? "This build can't install updates itself.";
            _logger.Warning($"Update requested but cannot be applied: {StatusText}");
            return;
        }

        IsBusy = true;
        HasError = false;
        IsProgressIndeterminate = true;
        DownloadPercent = 0;
        StatusText = "Downloading update…";
        try
        {
            var progress = new Progress<double>(fraction =>
            {
                IsProgressIndeterminate = false;
                DownloadPercent = Math.Clamp((int)Math.Round(fraction * 100), 0, 100);
                StatusText = $"Downloading update… {DownloadPercent}%";
            });
            var staged = await _updates.DownloadAndStageAsync(_available, progress).ConfigureAwait(true);

            StatusText = "Restarting to finish the update…";
            _logger.Information($"Applying update {staged.Version}.");
            // On the Steam Deck's AppImage this re-execs in place and never returns; on Windows/macOS
            // it launches a helper and returns, so we then exit to let the app's files unlock.
            _applier.ApplyAndRelaunch(staged);
            _requestExit();
        }
        catch (Exception ex)
        {
            _logger.Error("Installing the update failed.", ex);
            HasError = true;
            StatusText = "The update couldn't be installed. Try again later.";
            IsBusy = false;
            IsProgressIndeterminate = false;
        }
    }

    /// <summary>Hides the banner for now; the update can still be installed later from Settings.</summary>
    [RelayCommand]
    private void Dismiss() => IsBannerVisible = false;

    /// <summary>Remembers this version as skipped so the launch banner stays hidden for it.</summary>
    [RelayCommand]
    private void SkipVersion()
    {
        if (_available is { } available)
            PersistUpdateSettings(current => current with { SkippedVersion = available.Version.ToString() });
        IsBannerVisible = false;
    }

    private void RecordCheckTime() =>
        PersistUpdateSettings(current => current with { LastCheckUtc = DateTimeOffset.UtcNow });

    private void PersistUpdateSettings(Func<UpdateSettings, UpdateSettings> update)
    {
        try
        {
            _settings = _settingsService.Update(settings => settings with
            {
                Updates = update(settings.Updates),
            });
        }
        catch (Exception ex)
        {
            // Best-effort: a failed write must not break checking for or installing an update.
            _logger.Warning($"Could not persist update settings: {ex.Message}");
            _settings = _settings with { Updates = update(_settings.Updates) };
        }
    }
}
