using System.IO;
using Android.App;
using Android.Runtime;
using Avalonia;
using Avalonia.Android;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using EmuShelf.App.Android.Services;
using EmuShelf.Core.Storage;
using EmuShelf.Infrastructure.Diagnostics;
using EmuShelf.Infrastructure.Storage;

namespace EmuShelf.App.Android;

/// <summary>
/// The Android <c>Application</c> that hosts the shared <see cref="global::EmuShelf.App.App"/>.
/// This is where, before Avalonia starts, the single-view shell factory and the app-private storage
/// root are wired, and where the render backend is pinned — the single-view counterpart to the desktop
/// head's <c>Program.Main</c>.
/// </summary>
[Application]
public class EmuShelfAndroidApplication : AvaloniaAndroidApplication<global::EmuShelf.App.App>
{
    public EmuShelfAndroidApplication(IntPtr handle, JniHandleOwnership ownership)
        : base(handle, ownership)
    {
    }

    protected override AppBuilder CustomizeAppBuilder(AppBuilder builder)
    {
        // Wire (but do not yet run) the data-folder resolution. The user chooses an external folder on first
        // launch (D2); the base directory comes from a pointer persisted in app-private storage — the one
        // place always writable without a grant — plus the all-files grant and a writability probe. The
        // resolver itself runs only when an Activity asks for a view: this method executes at PROCESS
        // creation, which the system also does headlessly (accessibility re-bind after an install, a Shizuku
        // provider query), and a verdict frozen there was what the user later saw on the panel.
        var appPrivateFilesDir = FilesDir?.AbsolutePath ?? ApplicationContext?.FilesDir?.AbsolutePath;
        WireDataLocation(appPrivateFilesDir);

        // The single-view mirror of the desktop head's App.DesktopShellFactory registration.
        global::EmuShelf.App.App.SingleViewShellFactory =
            (lifetime, boot, deps) => new SingleViewShell(lifetime, boot, deps);

        // Analog-stick input for the shared poll loop. SDL (the desktop reader) cannot read Android input,
        // so the head supplies a MotionEvent-fed reader; the Activity feeds it via AndroidGamepadReader.Current.
        var gamepadReader = new AndroidGamepadReader();
        AndroidGamepadReader.Current = gamepadReader;
        global::EmuShelf.App.App.GamepadReaderFactory = () => gamepadReader;

        // Couch text entry raises the system IME (the desktop head launches an on-screen keyboard process
        // instead). Without it, gamepad search / rename cannot type. Resolves the live activity lazily —
        // it is set once the Activity resumes, after this builder runs.
        global::EmuShelf.App.App.OnScreenKeyboardFactory =
            () => new AndroidOnScreenKeyboardService(() => MainActivity.Current);

        // The managed Google Drive sign-in opens its consent page in the browser. The shared settings
        // view model does this with Process.Start(UseShellExecute), which throws on Android — so fire an
        // ACTION_VIEW intent instead. The browser then redirects to the loopback listener
        // (TcpLoopbackOAuthRedirectHandler) the transport binds.
        global::EmuShelf.App.App.ExternalUriOpener = OpenExternalUri;

        // In-app update on Android: hand the downloaded, checksum-verified APK to the system package
        // installer. There is no file-swap here (an app cannot overwrite its own installed APK), so the
        // shared UpdateApplierFactory — which only knows the desktop swap/re-exec strategies — is replaced
        // with the Android applier. The shared coordinator still drives the check + verified download.
        // Resolves the live Activity when there is one, else the application context (installer intent adds
        // NEW_TASK), mirroring OpenExternalUri.
        // NullAppLogger, not a FileAppLogger: constructing AppPaths would create empty Cache/Logs/… dirs
        // under the app-private folder (not the chosen data folder). The applier throws on failure and the
        // shared coordinator logs that via its own logger, so no log is lost.
        global::EmuShelf.App.App.UpdateApplierFactoryOverride =
            () => new AndroidUpdateApplier(
                () => (global::Android.Content.Context?)MainActivity.Current ?? ApplicationContext,
                global::EmuShelf.Core.Diagnostics.NullAppLogger.Instance);

        // Log-based performance tracing for the fan-on-scroll diagnosis. Routes PerfTrace to logcat (tag
        // EmuShelfPerf); the shell fills in the state provider once the couch view model exists. The sampler
        // is NOT started here — it is gated behind the same triple-L3 switch as the on-screen overlays
        // (RenderOverlayDiagnostics.Cycle), so a normal session writes nothing to the log. Works in Release
        // too, which is the point: the diagnostics can be turned on from a real build when needed.
        global::EmuShelf.App.Diagnostics.PerfTrace.Sink =
            message => global::Android.Util.Log.Info("EmuShelfPerf", message);

        // Settle-time GC for the couch grid glide. On MonoVM a minor collection during an ACTIVE
        // glide freezes the scroll ~100ms (busy threads are slow to reach safepoints) while the same
        // collection at rest costs ~1ms, so collect when the glide lands: throttled, and posted at
        // Background priority so the settle frame itself renders first. Pairs with the enlarged
        // nursery in environment.txt so the glide itself stays collection-free; desktop heads leave
        // this hook uninstalled because CoreCLR has no such pause. See DECISIONS 2026-08-31.
        var lastSettleCollect = 0L;
        global::EmuShelf.App.Services.PlatformIdleHints.ScrollGlideSettled = () =>
        {
            var now = global::System.Environment.TickCount64;
            if (now - lastSettleCollect < 2000)
                return;
            lastSettleCollect = now;
            global::Avalonia.Threading.Dispatcher.UIThread.Post(
                static () => global::System.GC.Collect(0),
                global::Avalonia.Threading.DispatcherPriority.Background);
        };

        return base.CustomizeAppBuilder(builder)
            .WithInterFont()
            // Pin EGL explicitly. The default [Egl, Software] list lets a failed EGL init fall back to
            // Software, where OpenGlControlBase returns false without throwing and the 3D shelf silently
            // never renders — the exact trap that hid the macOS/Metal bug. Dropping Software makes a GL
            // failure loud (InitializationFailed) instead of a flat fallback. 0a confirmed EGL yields an
            // OpenGL ES 3.0 context on this backend.
            .With(new AndroidPlatformOptions
            {
                RenderingMode = [AndroidRenderingMode.Egl],
            });
    }

    // Opens a URL in the system browser via an ACTION_VIEW intent. Uses the live Activity when there is
    // one (a real Activity context); falls back to the application context with NEW_TASK otherwise.
    private void OpenExternalUri(System.Uri uri)
    {
        // AbsoluteUri, not ToString(): the OAuth URL's query carries percent-encoded redirect_uri/scope/
        // state, and Uri.ToString() can unescape them; AbsoluteUri stays round-trip-safe.
        using var data = global::Android.Net.Uri.Parse(uri.AbsoluteUri);
        using var intent = new global::Android.Content.Intent(global::Android.Content.Intent.ActionView, data);
        if (MainActivity.Current is { } activity)
        {
            activity.StartActivity(intent);
            return;
        }
        // From a non-Activity context NEW_TASK is required. If there is no context at all, throw rather
        // than silently no-op — the caller (LaunchSignIn) cancels the connect on a throw, so the sign-in
        // fails fast instead of hanging on a redirect that will never come.
        intent.AddFlags(global::Android.Content.ActivityFlags.NewTask);
        var context = ApplicationContext
            ?? throw new global::System.InvalidOperationException("No Android context is available to open the sign-in page.");
        context.StartActivity(intent);
    }

    // Resolves the data folder (or triggers onboarding) and sets the matching App hook. Split out of
    // CustomizeAppBuilder so the flow reads top-to-bottom.
    private void WireDataLocation(string? appPrivateFilesDir)
    {
        if (string.IsNullOrEmpty(appPrivateFilesDir))
        {
            // With no app-private files dir there is nowhere to keep the pointer; fail fast the same way the
            // bootstrapper does for a blank override rather than booting into a broken state.
            return;
        }

        // A pre-boot logger in app-private storage (the composition-root logger does not exist yet, and its
        // Logs/ live in the not-yet-chosen data folder). Reaches only Logs/ under FilesDir — no other app
        // dirs are created there — so onboarding, permission, and resolve diagnostics are readable via adb
        // even before a folder is picked. FileAppLogger swallows its own I/O errors, so this is safe.
        var prebootLogger = new FileAppLogger(new AppPaths(appPrivateFilesDir));
        // The pointer's primary copy is app-private; its mirror is a dotfile on primary shared storage, which
        // an uninstall leaves alone. That is what lets a reinstall find its library again instead of
        // re-running onboarding over it (it needs the all-files grant to be readable, so the grant step still
        // runs after a reinstall — but the folder pick does not).
        var store = new JsonDataLocationStore(
            Path.Combine(appPrivateFilesDir, "data-location.json"),
            DataLocationMirrorPath(),
            prebootLogger);
        var permission = new AndroidStoragePermissionService(() => ApplicationContext, prebootLogger);
        var resolver = new DataLocationResolver(store, permission, DirectoryWritability.IsWritable);

        // The bootstrap is always exposed: the composition root resolves through it when the Activity asks
        // for its first view, onboarding re-resolves through it on foreground return, and the Settings
        // "change data folder" row re-runs its SAF pick on a device that has long finished onboarding.
        var bootstrap = new AndroidDataLocationBootstrap(
            store, permission, ResolveShellTopLevel, resolver.Resolve, prebootLogger);
        global::EmuShelf.App.App.DataLocation = bootstrap;

        // The process restart used both to hand off from onboarding to the real shell (the single-view host
        // won't swap a live-reassigned MainView, so the composed shell must come up fresh) and to reboot into
        // the newly chosen folder after Settings re-points it.
        global::EmuShelf.App.App.RestartRequested = () => AndroidAppRelaunch.Restart(ApplicationContext);

        // Both Activity-side hooks start out routed to onboarding, unconditionally: whether onboarding will
        // show is not known here (see above), and SingleViewShell overwrites both the moment the real shell
        // composes. Until then the foreground callback drives the grant refresh (and the auto-complete when
        // the pointer becomes readable), and the couch controller reaches the onboarding card — late-bound
        // through the App hook, which is null (a no-op) while no onboarding view-model exists.
        AndroidActivityLifecycle.ReturnedToForeground = bootstrap.NotifyForegroundReturned;
        AndroidGamepadInput.Dispatch = action =>
            global::EmuShelf.App.App.OnboardingGamepadDispatch?.Invoke(action) ?? false;
        // The D-pad (a hat axis on the Thor) and the left stick reach the setup page through the motion
        // reader, since no shell poll loop exists yet to read it. A no-op once the shell is up.
        AndroidGamepadReader.PreShellNavigate = action =>
            global::EmuShelf.App.App.OnboardingGamepadDispatch?.Invoke(action) ?? false;
    }

    // <primary shared storage>/.emushelf-data-location.json — pointer-independent, survives an uninstall,
    // hidden from file managers by the leading dot. Null (no mirror) if the primary volume is unknown.
    private static string? DataLocationMirrorPath()
    {
        var primary = global::Android.OS.Environment.ExternalStorageDirectory?.AbsolutePath;
        return string.IsNullOrEmpty(primary) ? null : Path.Combine(primary, ".emushelf-data-location.json");
    }

    // The live Activity's content — the onboarding view before a folder is picked, the composed shell's
    // MainView after — is what the folder picker's TopLevel is reached through. Read from the Activity, not
    // from ISingleViewApplicationLifetime.MainView: the views come from IActivityApplicationLifetime's
    // MainViewFactory, and Avalonia's Android lifetime leaves MainView null on that path (it only mirrors a
    // view that was assigned to MainView directly), so the old lookup returned null once the shell was up.
    private static TopLevel? ResolveShellTopLevel() =>
        MainActivity.Current?.Content is Control view ? TopLevel.GetTopLevel(view) : null;
}
