using EmuShelf.App.ViewModels;
using EmuShelf.Core.Achievements;
using EmuShelf.Core.Launching;
using EmuShelf.Core.Metadata;
using EmuShelf.Core.Metadata.ScreenScraper;
using EmuShelf.Core.Settings;

namespace EmuShelf.App.Services;

/// <summary>
/// The window/view-layer collaborators the shared composition root cannot build itself, because
/// they touch a concrete <see cref="Avalonia.Controls.Window"/> or the desktop lifetime. The
/// desktop head builds these from a real <c>MainWindow</c> and its dialog windows; a future
/// single-view (Android) head will build view-based equivalents. Keeping the contract here lets
/// <see cref="App"/> compose the whole graph identically under either Avalonia lifetime.
/// </summary>
public interface IPlatformShell
{
    /// <summary>Gamepad/desktop interface-mode state, driven by the surface's window state.</summary>
    IInterfaceModeService InterfaceMode { get; }

    /// <summary>Brings the emulator to the foreground and restores the shell on return.</summary>
    IFrontendController Frontend { get; }

    /// <summary>Application-lifetime operations (shutdown) the view models request.</summary>
    IApplicationLifetimeService Lifetime { get; }

    /// <summary>File/folder pickers and modal dialogs, owned by the surface's top level.</summary>
    IDialogService Dialog { get; }

    /// <summary>
    /// Attaches the fully-built <paramref name="viewModel"/> to the surface and shows it. The shell
    /// wires <paramref name="callbacks"/> to its own surface lifecycle — on desktop that is the
    /// window's <c>Opened</c>/<c>Closing</c> events and the application's <c>Exit</c> event.
    /// </summary>
    void Show(MainViewModel viewModel, ShellCallbacks callbacks);
}

/// <summary>
/// Lifecycle hooks the shared composition root hands to the shell so behaviour that belongs to the
/// shared graph (startup refreshes, save flushing, disposal) runs at the right surface moments
/// without the shell knowing what they do.
/// </summary>
/// <param name="Opened">Run once the surface first appears (background startup refreshes).</param>
/// <param name="Closing">Run as the surface begins to close (flush pending saves).</param>
/// <param name="Exit">Run on application exit (dispose shared disposables, final log).</param>
public sealed record ShellCallbacks(
    Action Opened,
    Action Closing,
    Action Exit);

/// <summary>
/// The subset of the shared service graph a platform shell needs to build its window-typed
/// collaborators — chiefly the dialog service, which bridges shared providers to head-owned dialog
/// windows. Everything else a shell needs lives on <see cref="AppBootstrapper"/>.
/// </summary>
public sealed record PlatformShellDependencies(
    IRetroAchievementsDetailsService RetroAchievementsDetails,
    IRetroAchievementsAccountService RetroAchievementsAccount,
    IRetroAchievementsBadgeCache RetroAchievementsBadges,
    IGameArtworkSearchProvider WebArtworkSearch,
    IRemoteArtworkDownloader WebArtworkDownloader,
    IGameScrapeApplicationService ScrapeApply,
    IScreenScraperAccountService ScreenScraperAccount,
    IScreenScraperBatchService? ScrapeBatch);
