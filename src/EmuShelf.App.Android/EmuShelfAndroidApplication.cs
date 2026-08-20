using System;
using Android.App;
using Android.Runtime;
using Avalonia;
using Avalonia.Android;
using EmuShelf.App.Android.Services;

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
        // App-private files dir is the only reliably writable portable root on Android; AppPaths cannot
        // obtain it without the Android context, so inject it here before the composition root runs.
        global::EmuShelf.App.App.BaseDirectoryOverride =
            FilesDir?.AbsolutePath ?? ApplicationContext?.FilesDir?.AbsolutePath;

        // The single-view mirror of the desktop head's App.DesktopShellFactory registration.
        global::EmuShelf.App.App.SingleViewShellFactory =
            (lifetime, boot, deps) => new SingleViewShell(lifetime, boot, deps);

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
}
