using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using EmuShelf.Core.Diagnostics;
using EmuShelf.Core.Emulators;

namespace EmuShelf.App.ViewModels;

/// <summary>
/// One emulator row in the Install Emulators settings section: shows install/update status and drives the
/// install/update action with live download progress. The heavy lifting is in
/// <see cref="IEmulatorInstallService"/>; this row is view state plus the busy/progress book-keeping.
/// </summary>
public partial class EmulatorInstallRowViewModel : ViewModelBase
{
    private readonly IEmulatorInstallService _service;
    private readonly IAppLogger _logger;
    private readonly Func<string, string, Task>? _onInstalled;
    private readonly Func<string, Task>? _openDownloadPage;

    public string EmulatorId { get; }
    public string EmulatorName { get; }

    public EmulatorInstallRowViewModel(
        string emulatorId,
        string emulatorName,
        IEmulatorInstallService service,
        IAppLogger logger,
        Func<string, string, Task>? onInstalled = null,
        Func<string, Task>? openDownloadPage = null)
    {
        EmulatorId = emulatorId;
        EmulatorName = emulatorName;
        _service = service;
        _logger = logger;
        _onInstalled = onInstalled;
        _openDownloadPage = openDownloadPage;
    }

    [ObservableProperty]
    public partial string StatusText { get; set; } = "Checking…";

    [ObservableProperty]
    public partial string? InstalledVersion { get; set; }

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(InstallCommand))]
    [NotifyCanExecuteChangedFor(nameof(UpdateCommand))]
    [NotifyPropertyChangedFor(nameof(IsIdle))]
    public partial bool IsBusy { get; set; }

    [ObservableProperty]
    public partial bool IsProgressIndeterminate { get; set; }

    [ObservableProperty]
    public partial int DownloadPercent { get; set; }

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(InstallCommand))]
    public partial bool CanInstall { get; set; }

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(UpdateCommand))]
    public partial bool CanUpdate { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasDownloadPage))]
    public partial string? DownloadPageUrl { get; set; }

    public bool HasDownloadPage => !string.IsNullOrWhiteSpace(DownloadPageUrl);

    public bool IsIdle => !IsBusy;

    /// <summary>Re-reads this emulator's install status and updates the row's buttons/labels.</summary>
    public async Task RefreshAsync(CancellationToken cancellationToken = default)
    {
        // A refresh while an install/update is running would read a not-yet-written manifest and reset
        // the progress bar and buttons mid-download; the post-action resync runs after IsBusy clears.
        if (IsBusy)
            return;

        try
        {
            // Offload the synchronous prefix (manifest read + the user-install probe's DB read) and the
            // network call to a worker; Apply then resumes on the UI thread to update bound properties.
            var status = await Task.Run(() => _service.GetStatusAsync(EmulatorId, cancellationToken), cancellationToken);
            Apply(status);
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            _logger.Error($"Couldn't check the install status for {EmulatorName}.", ex);
            SetStatus("Couldn't check for a build.", canInstall: false, canUpdate: false, downloadPage: null, version: null);
        }
    }

    private void Apply(EmulatorInstallStatus status)
    {
        switch (status)
        {
            case EmulatorInstallStatus.NotInstalled notInstalled:
                SetStatus(
                    notInstalled.LatestVersion is { } latest ? $"Not installed · latest {latest}" : "Not installed",
                    canInstall: true, canUpdate: false, downloadPage: null, version: null);
                break;
            case EmulatorInstallStatus.Managed managed:
                // No "up to date" claim here — a managed install whose latest-build check could not reach
                // the source also lands on Managed, and we must not assert freshness we didn't verify.
                SetStatus($"Installed · {managed.InstalledVersion}",
                    canInstall: false, canUpdate: false, downloadPage: null, version: managed.InstalledVersion);
                break;
            case EmulatorInstallStatus.UpdateAvailable update:
                SetStatus($"Update available · {update.InstalledVersion} → {update.LatestVersion}",
                    canInstall: false, canUpdate: true, downloadPage: null, version: update.InstalledVersion);
                break;
            case EmulatorInstallStatus.UserProvided userProvided:
                SetStatus(
                    userProvided.LatestVersion is { } userLatest
                        ? $"Using your own install · latest {userLatest}"
                        : "Using your own install",
                    canInstall: false, canUpdate: false, downloadPage: null, version: null);
                break;
            case EmulatorInstallStatus.Unsupported unsupported:
                SetStatus(unsupported.Reason,
                    canInstall: false, canUpdate: false, downloadPage: unsupported.DownloadPageUrl, version: null);
                break;
            case EmulatorInstallStatus.CheckFailed failed:
                SetStatus(failed.Reason, canInstall: false, canUpdate: false, downloadPage: null, version: null);
                break;
        }
    }

    private void SetStatus(string text, bool canInstall, bool canUpdate, string? downloadPage, string? version)
    {
        StatusText = text;
        CanInstall = canInstall;
        CanUpdate = canUpdate;
        DownloadPageUrl = downloadPage;
        InstalledVersion = version;
        DownloadPercent = 0;
        IsProgressIndeterminate = false;
    }

    [RelayCommand(CanExecute = nameof(CanRunInstall))]
    private Task InstallAsync() => RunAsync(isUpdate: false);

    [RelayCommand(CanExecute = nameof(CanRunUpdate))]
    private Task UpdateAsync() => RunAsync(isUpdate: true);

    private bool CanRunInstall() => CanInstall && !IsBusy;

    private bool CanRunUpdate() => CanUpdate && !IsBusy;

    private async Task RunAsync(bool isUpdate)
    {
        if (IsBusy)
            return;

        IsBusy = true;
        IsProgressIndeterminate = true;
        DownloadPercent = 0;
        StatusText = isUpdate ? "Updating…" : "Downloading…";
        var progress = new Progress<double>(fraction =>
        {
            IsProgressIndeterminate = false;
            DownloadPercent = Math.Clamp((int)Math.Round(fraction * 100), 0, 100);
            StatusText = $"Downloading… {DownloadPercent}%";
        });

        var resyncFromStatus = false;
        try
        {
            // Offload the download/extract work; the Progress<double> marshals percent updates back to
            // the UI thread, and the switch below resumes there too.
            var result = await Task.Run(() => isUpdate
                ? _service.UpdateAsync(EmulatorId, progress)
                : _service.InstallAsync(EmulatorId, progress));
            switch (result)
            {
                case EmulatorInstallResult.Installed installed:
                    // Config auto-wiring is best-effort; its failure must not report a completed install
                    // as failed.
                    if (_onInstalled is not null)
                    {
                        try
                        {
                            await _onInstalled(EmulatorId, installed.ExecutablePath);
                        }
                        catch (Exception ex)
                        {
                            _logger.Error($"Auto-configuring {EmulatorName} after install failed.", ex);
                        }
                    }
                    StatusText = $"Installed · {installed.Version}";
                    resyncFromStatus = true;
                    break;
                case EmulatorInstallResult.AlreadyCurrent current:
                    StatusText = $"Already up to date · {current.Version}";
                    resyncFromStatus = true;
                    break;
                case EmulatorInstallResult.Refused refused:
                    // Keep the reason visible — do not resync, which would replace it with the plain status.
                    StatusText = refused.Reason;
                    break;
                case EmulatorInstallResult.Failed failed:
                    StatusText = failed.Reason;
                    break;
            }
        }
        catch (Exception ex)
        {
            _logger.Error($"Installing {EmulatorName} failed.", ex);
            StatusText = $"Install failed: {ex.Message}";
        }
        finally
        {
            IsBusy = false;
            IsProgressIndeterminate = false;
        }

        // Resync buttons/labels from the authoritative status only after a successful install/update.
        if (resyncFromStatus)
            await RefreshAsync();
    }

    [RelayCommand]
    private async Task OpenDownloadPageAsync()
    {
        if (_openDownloadPage is null || string.IsNullOrWhiteSpace(DownloadPageUrl))
            return;
        try
        {
            await _openDownloadPage(DownloadPageUrl);
        }
        catch (Exception ex)
        {
            _logger.Error($"Couldn't open the download page for {EmulatorName}.", ex);
        }
    }
}
