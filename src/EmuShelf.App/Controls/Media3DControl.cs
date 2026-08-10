using System.Numerics;
using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.OpenGL;
using Avalonia.OpenGL.Controls;
using Avalonia.Platform;
using EmuShelf.Rendering;
using EmuShelf.Rendering.Gl;
using EmuShelf.Rendering.Models;
using EmuShelf.Rendering.Shells;
using Silk.NET.Core.Contexts;
using GL = Silk.NET.OpenGL.GL;

namespace EmuShelf.App.Controls;

/// <summary>
/// The couch shelf's focused hero: the game's physical medium, rendered on the GPU and turnable.
/// </summary>
/// <remarks>
/// Avalonia owns the GL context and hands one to <see cref="OnOpenGlRender"/> along with the
/// framebuffer to draw into; this control only adapts that to <see cref="MediaShellRenderer"/>,
/// which does the actual work and knows nothing about Avalonia. If the context cannot be brought up
/// — a driver that will not serve GLES 3.0, a remote session, a headless run — the control raises
/// <see cref="InitializationFailed"/> and the shelf puts every game back on its flat cover.
/// </remarks>
public sealed class Media3DControl : OpenGlControlBase
{
    public static readonly StyledProperty<MediaShell?> ShellProperty =
        AvaloniaProperty.Register<Media3DControl, MediaShell?>(nameof(Shell));

    public static readonly StyledProperty<IImage?> CoverProperty =
        AvaloniaProperty.Register<Media3DControl, IImage?>(nameof(Cover));

    public static readonly StyledProperty<Color> AccentProperty =
        AvaloniaProperty.Register<Media3DControl, Color>(nameof(Accent), Colors.Gray);

    public static readonly StyledProperty<double> YawProperty =
        AvaloniaProperty.Register<Media3DControl, double>(nameof(Yaw));

    public static readonly StyledProperty<double> PitchProperty =
        AvaloniaProperty.Register<Media3DControl, double>(nameof(Pitch));

    private GL? _gl;
    private MediaShellRenderer? _renderer;
    private bool _failed;
    private IImage? _uploadedCover;
    private Color _uploadedAccent;

    /// <summary>Raised on the UI thread when the GPU path is unavailable for this session.</summary>
    public event EventHandler? InitializationFailed;

    /// <summary>
    /// Asks for a fresh GL frame whenever what the hero should show changes.
    /// </summary>
    /// <remarks>
    /// <c>AffectsRender</c> is the obvious tool here and it is the wrong one. It calls
    /// <c>Visual.InvalidateVisual()</c>, which is non-virtual, and <see cref="OpenGlControlBase"/>
    /// hides rather than overrides it — so the base call marks the compositor dirty, the existing
    /// framebuffer is blitted again unchanged, and <see cref="OnOpenGlRender"/> is never reached.
    /// The hero then renders once and freezes on whatever game happened to be focused first, which
    /// looks exactly like the shell and cover being hard-coded. Only
    /// <see cref="OpenGlControlBase.RequestNextFrameRendering"/> schedules a real GL frame.
    /// </remarks>
    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);

        if (change.Property == ShellProperty
            || change.Property == CoverProperty
            || change.Property == AccentProperty
            || change.Property == YawProperty
            || change.Property == PitchProperty
            || change.Property == BoundsProperty)
        {
            RequestNextFrameRendering();
        }
    }

    /// <summary>Which medium to draw; null draws nothing.</summary>
    public MediaShell? Shell
    {
        get => GetValue(ShellProperty);
        set => SetValue(ShellProperty, value);
    }

    /// <summary>Cover art printed on the shell's front panel.</summary>
    public IImage? Cover
    {
        get => GetValue(CoverProperty);
        set => SetValue(CoverProperty, value);
    }

    /// <summary>The focused system's accent, tinting the studio and the shell's unprinted faces.</summary>
    public Color Accent
    {
        get => GetValue(AccentProperty);
        set => SetValue(AccentProperty, value);
    }

    /// <summary>Rotation about the shell's up axis, in radians. 0 faces the viewer.</summary>
    public double Yaw
    {
        get => GetValue(YawProperty);
        set => SetValue(YawProperty, value);
    }

    /// <summary>Rotation about the shell's right axis, in radians.</summary>
    public double Pitch
    {
        get => GetValue(PitchProperty);
        set => SetValue(PitchProperty, value);
    }

    protected override void OnOpenGlInit(GlInterface gl)
    {
        try
        {
            // Silk.NET resolves its entry points through Avalonia's loader, so both talk to the one
            // context Avalonia made current — no second context, no sharing to get wrong.
            _gl = GL.GetApi(new LamdaNativeContext(name => gl.GetProcAddress(name)));

            var version = GlVersion;
            var dialect = version.Type == GlProfileType.OpenGLES
                ? GlslDialect.Es300
                : GlslDialect.Desktop;

            _renderer = MediaShellRenderer.Create(
                _gl, dialect, version.Major, version.Minor, ToLinear(Accent));
            _uploadedAccent = Accent;
            UploadCover();
        }
        catch (Exception)
        {
            // A missing GPU path is a supported outcome, not a crash: the shelf still works with
            // flat covers, so swallow it and tell the library to fall back.
            _renderer = null;
            _gl = null;
            Fail();
        }
    }

    protected override void OnOpenGlRender(GlInterface gl, int fb)
    {
        if (_renderer is null || Shell is not { } shell)
        {
            return;
        }

        // Render at device pixels, not layout units, so the hero is sharp on a HiDPI panel.
        var scaling = (VisualRoot as TopLevel)?.RenderScaling ?? 1.0;
        var width = (uint)Math.Max(1, Math.Round(Bounds.Width * scaling));
        var height = (uint)Math.Max(1, Math.Round(Bounds.Height * scaling));

        try
        {
            if (Accent != _uploadedAccent)
            {
                _renderer.SetAccent(ToLinear(Accent));
                _uploadedAccent = Accent;
            }

            if (!ReferenceEquals(Cover, _uploadedCover))
            {
                UploadCover();
            }

            _renderer.Render(shell, (uint)fb, width, height, (float)Yaw, (float)Pitch);
        }
        catch (Exception)
        {
            _renderer.Dispose();
            _renderer = null;
            Fail();
        }
    }

    protected override void OnOpenGlDeinit(GlInterface gl)
    {
        _renderer?.Dispose();
        _renderer = null;
        _gl = null;
        _uploadedCover = null;
    }

    protected override void OnOpenGlLost()
    {
        // The context went away under us (a GPU reset, a display change). Drop everything; Avalonia
        // will call OnOpenGlInit again if it can rebuild one.
        _renderer = null;
        _gl = null;
        _uploadedCover = null;
    }

    private void Fail()
    {
        if (_failed)
        {
            return;
        }

        _failed = true;
        InitializationFailed?.Invoke(this, EventArgs.Empty);
    }

    private void UploadCover()
    {
        _renderer?.SetCoverArt(Cover is Bitmap bitmap ? ToTextureImage(bitmap) : null);
        _uploadedCover = Cover;
    }

    private static Vector3 ToLinear(Color colour) =>
        MediaShellRenderer.ToLinear(colour.R / 255f, colour.G / 255f, colour.B / 255f);

    /// <summary>Copies an Avalonia bitmap into the straight-alpha RGBA the renderer uploads.</summary>
    /// <remarks>
    /// Avalonia hands out premultiplied BGRA. The shader treats the cover as an sRGB colour texture
    /// and does its own blending, so the channels are reordered and the premultiplication undone
    /// here — leaving it in would darken every cover in proportion to its own alpha.
    /// </remarks>
    private static TextureImage? ToTextureImage(Bitmap bitmap)
    {
        var size = bitmap.PixelSize;
        if (size.Width <= 0 || size.Height <= 0)
        {
            return null;
        }

        var stride = size.Width * 4;
        var pixels = new byte[stride * size.Height];

        // Pinned rather than `fixed`, so the app project keeps unsafe code switched off; only the
        // renderer, which genuinely needs raw pointers for GL uploads, enables it.
        var handle = GCHandle.Alloc(pixels, GCHandleType.Pinned);
        try
        {
            bitmap.CopyPixels(
                new PixelRect(0, 0, size.Width, size.Height),
                handle.AddrOfPinnedObject(),
                pixels.Length,
                stride);
        }
        finally
        {
            handle.Free();
        }

        var swapRedAndBlue = bitmap.Format != PixelFormat.Rgba8888;
        var premultiplied = bitmap.AlphaFormat != AlphaFormat.Unpremul;

        for (var i = 0; i < pixels.Length; i += 4)
        {
            if (swapRedAndBlue)
            {
                (pixels[i], pixels[i + 2]) = (pixels[i + 2], pixels[i]);
            }

            var alpha = pixels[i + 3];
            if (premultiplied && alpha is > 0 and < 255)
            {
                pixels[i] = (byte)Math.Min(255, pixels[i] * 255 / alpha);
                pixels[i + 1] = (byte)Math.Min(255, pixels[i + 1] * 255 / alpha);
                pixels[i + 2] = (byte)Math.Min(255, pixels[i + 2] * 255 / alpha);
            }
        }

        return new TextureImage { Width = size.Width, Height = size.Height, Rgba = pixels };
    }
}
