using Avalonia;
using Avalonia.Controls;
using Avalonia.Platform;
using EmuShelf.Core.SecondScreen;

namespace EmuShelf.App.Controls;

public sealed record EmbeddedCompanionVideo(IPlatformHandle Handle, ICompanionVideoPlayer Player);

/// <summary>The native video surface is supplied by the platform head; desktop still builds without it.</summary>
public sealed class CompanionVideoHost : NativeControlHost
{
    public static Func<IPlatformHandle, Action<string>, Action<double, double>, EmbeddedCompanionVideo>? Factory { get; set; }
    public static readonly StyledProperty<string?> SourceProperty = AvaloniaProperty.Register<CompanionVideoHost, string?>(nameof(Source));
    public string? Source { get => GetValue(SourceProperty); set => SetValue(SourceProperty, value); }
    public event Action<string>? PlaybackFailed;
    public event Action<double, double>? Swiped;
    private EmbeddedCompanionVideo? _video;

    protected override IPlatformHandle CreateNativeControlCore(IPlatformHandle parent)
    {
        _video = Factory?.Invoke(parent, message => PlaybackFailed?.Invoke(message), (x, y) => Swiped?.Invoke(x, y));
        if (_video is null) return base.CreateNativeControlCore(parent);
        if (Source is { } source) _video.Player.Play(source);
        return _video.Handle;
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == SourceProperty && _video is not null)
        {
            if (Source is { } path) _video.Player.Play(path);
            else _video.Player.Stop();
        }
    }

    protected override void DestroyNativeControlCore(IPlatformHandle control)
    {
        _video?.Player.Dispose();
        _video = null;
        base.DestroyNativeControlCore(control);
    }
}
