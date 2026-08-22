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
        // Decide where EmuShelf keeps its data before Avalonia starts. The user chooses an external folder
        // on first launch (D2), so the base directory is resolved from a pointer persisted in app-private
        // storage — the one place always writable without a grant — plus the all-files grant and a
        // writability probe. Resolved → boot against the chosen folder; otherwise hand the composition root
        // a bootstrap so it shows onboarding instead of opening the database against nothing.
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

        // Log-based performance tracing for the fan-on-scroll diagnosis. Routes PerfTrace to logcat (tag
        // EmuShelfPerf) and starts the once-a-second sampler now; the shell fills in the state provider once
        // the couch view model exists. Runs in Release too — the whole point is to read it off a real build.
        global::EmuShelf.App.Diagnostics.PerfTrace.Sink =
            message => global::Android.Util.Log.Info("EmuShelfPerf", message);
        global::EmuShelf.App.Diagnostics.PerfTrace.StartSampling();

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
        var store = new JsonDataLocationStore(Path.Combine(appPrivateFilesDir, "data-location.json"), prebootLogger);
        var permission = new AndroidStoragePermissionService(() => ApplicationContext);
        var resolver = new DataLocationResolver(store, permission, DirectoryWritability.IsWritable);
        var resolution = resolver.Resolve();

        if (resolution.IsResolved)
        {
            global::EmuShelf.App.App.BaseDirectoryOverride = resolution.BaseDirectory;
            return;
        }

        var bootstrap = new AndroidDataLocationBootstrap(
            store, permission, ResolveOnboardingTopLevel, resolution, prebootLogger);
        global::EmuShelf.App.App.DataLocation = bootstrap;

        // Hand off from onboarding to the real shell via a process restart: the single-view host won't swap
        // a live-reassigned MainView, so the composed shell must come up fresh (it then resolves the pointer
        // onboarding just wrote and boots to the library).
        global::EmuShelf.App.App.RestartRequested = () => AndroidAppRelaunch.Restart(ApplicationContext);

        // While onboarding is up the shell's return signal isn't wired yet, so point the Activity's
        // foreground callback at the grant refresh; SingleViewShell re-points it once the real UI composes.
        AndroidActivityLifecycle.ReturnedToForeground = bootstrap.NotifyForegroundReturned;

        // Route the couch controller into onboarding too — the shared shell's dispatcher does not exist yet,
        // so without this the D-pad and A button would be dead on the first screen on a gamepad-first device.
        // Late-bound through the App hook (set once the onboarding view-model is built); SingleViewShell
        // overwrites Dispatch with the real view-model once a folder is chosen.
        AndroidGamepadInput.Dispatch = action =>
            global::EmuShelf.App.App.OnboardingGamepadDispatch?.Invoke(action) ?? false;
    }

    // The onboarding view is the single-view MainView until a folder is picked, so the folder picker's
    // TopLevel is reached through it.
    private static TopLevel? ResolveOnboardingTopLevel() =>
        (global::Avalonia.Application.Current?.ApplicationLifetime as ISingleViewApplicationLifetime)?.MainView is { } view
            ? TopLevel.GetTopLevel(view)
            : null;
}
