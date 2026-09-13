using Avalonia.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using EmuShelf.Core.SecondScreen;

namespace EmuShelf.App.ViewModels;

public sealed partial class SecondScreenViewModel
{
    [ObservableProperty] public partial string SpotlightTitle { get; set; } = "EmuShelf";
    private CompanionMediaSet? _media;
    private CancellationTokenSource? _mediaLoad;
    private long _mediaGeneration;
    private int _mediaIndex;
    public Func<CompanionMediaItem, CancellationToken, Task<string?>>? ResolveMediaPath { get; set; }
    public Action? MediaFetchRequested { get; set; }
    public Action? MediaOpening { get; set; }
    [ObservableProperty] public partial bool IsMediaChromeVisible { get; set; } = true;
    public bool IsDockVisible => !IsMediaOpen && !IsAchievementsOpen;
    public bool IsSpotlight => Overlay == SecondScreenOverlayKind.None;
    public bool IsMediaOpen => Overlay == SecondScreenOverlayKind.Media;
    public bool HasMedia => _media?.Items.Count > 0;
    public bool CanBrowseMedia => _media?.Items.Count > 1;
    public string MediaTitle => _media?.Title ?? "Media";
    public string MediaCaption => HasMedia ? $"{_mediaIndex + 1} / {_media!.Items.Count} · {_media.Items[_mediaIndex].Title}" : "No media yet";
    public bool IsSelectedVideo => HasMedia && _media!.Items[_mediaIndex].IsVideo;
    public bool IsVideoPlaying => PlayingVideoPath is not null;
    public bool ShowMediaPlay => IsSelectedVideo && PlayingVideoPath is null && !IsMediaLoading;

    [ObservableProperty] public partial Bitmap? MediaImage { get; set; }
    [ObservableProperty] public partial string? MediaStatus { get; set; }
    [ObservableProperty] public partial bool IsFetchingMedia { get; set; }
    [ObservableProperty] public partial bool CanFetchMedia { get; set; }
    [ObservableProperty] public partial bool IsMediaLoading { get; set; }
    [ObservableProperty] public partial string? PlayingVideoPath { get; set; }
    partial void OnPlayingVideoPathChanged(string? value)
    {
        if (value is not null) IsMediaChromeVisible = true;
        OnPropertyChanged(nameof(ShowMediaPlay));
        OnPropertyChanged(nameof(IsVideoPlaying));
    }
    partial void OnIsMediaLoadingChanged(bool value) => OnPropertyChanged(nameof(ShowMediaPlay));
    partial void OnMediaImageChanging(Bitmap? value)
    {
        if (!ReferenceEquals(MediaImage, value)) MediaImage?.Dispose();
    }

    public void SetMedia(CompanionMediaSet media)
    {
        // Open with gameplay imagery; packaging remains available further in the gallery.
        media = media with { Items = media.Items.OrderBy(i =>
            i.Kind == EmuShelf.Core.Metadata.GameMediaKind.Screenshot ? 0 : 1).ToArray() };
        if (_media?.GameId == media.GameId && _media.Title == media.Title && _media.Items.SequenceEqual(media.Items)) return;
        var sameGame = _media?.GameId == media.GameId;
        var previous = sameGame && HasMedia ? _media!.Items[_mediaIndex] : null;
        _media = media;
        _mediaIndex = previous is null ? 0 : Math.Max(0, media.Items.ToList().FindIndex(i => i == previous));
        NotifyMediaChanged();
        if (IsMediaOpen) _ = LoadMediaAsync(play: false);
        else StopMedia();
    }

    public void ClearMedia()
    {
        _media = null;
        _mediaIndex = 0;
        StopMedia();
        NotifyMediaChanged();
    }

    [RelayCommand]
    private void OpenMedia()
    {
        IsMediaChromeVisible = true;
        MediaOpening?.Invoke();
        Overlay = SecondScreenOverlayKind.Media;
        _ = LoadMediaAsync(play: false);
    }

    [RelayCommand] private void FetchMedia() => MediaFetchRequested?.Invoke();
    [RelayCommand] private void NextMedia() => MoveMedia(1);
    [RelayCommand] private void PreviousMedia() => MoveMedia(-1);
    [RelayCommand] private Task PlayMediaAsync() => LoadMediaAsync(play: true);
    [RelayCommand] private void StopVideo() => PlayingVideoPath = null;

    public void TapMedia()
    {
        if (IsMediaOpen && HasMedia && !IsVideoPlaying) IsMediaChromeVisible = !IsMediaChromeVisible;
    }

    public void SwipeMedia(double horizontal, double vertical)
    {
        if (IsMediaOpen && vertical >= 64 && vertical > Math.Abs(horizontal) * 1.5)
        {
            CloseOverlayCommand.Execute(null);
            return;
        }
        if (Math.Abs(horizontal) < 48 || Math.Abs(horizontal) < Math.Abs(vertical) * 1.5 ||
            (Overlay != SecondScreenOverlayKind.None && !IsMediaOpen)) return;
        if (!IsMediaOpen) OpenMedia();
        else MoveMedia(horizontal < 0 ? 1 : -1);
    }

    private void MoveMedia(int direction)
    {
        if (!HasMedia) return;
        _mediaIndex = (_mediaIndex + direction + _media!.Items.Count) % _media.Items.Count;
        NotifyMediaChanged();
        _ = LoadMediaAsync(play: false);
    }

    private void NotifyMediaChanged()
    {
        OnPropertyChanged(nameof(HasMedia));
        OnPropertyChanged(nameof(CanBrowseMedia));
        OnPropertyChanged(nameof(MediaTitle));
        OnPropertyChanged(nameof(MediaCaption));
        OnPropertyChanged(nameof(IsSelectedVideo));
        OnPropertyChanged(nameof(ShowMediaPlay));
    }

    private async Task LoadMediaAsync(bool play)
    {
        StopMedia();
        if (!IsMediaOpen || !HasMedia) return;
        var item = _media!.Items[_mediaIndex];
        // Preview with a screenshot; the video itself remains strictly tap-to-download.
        var poster = item.IsVideo && !play;
        if (poster)
        {
            var screenshot = _media.Items.FirstOrDefault(i => i.Kind == EmuShelf.Core.Metadata.GameMediaKind.Screenshot);
            if (screenshot is null) return;
            item = screenshot;
        }
        var generation = _mediaGeneration;
        var cancellation = _mediaLoad = new CancellationTokenSource();
        IsMediaLoading = true;
        MediaStatus = "Loading…";
        try
        {
            var path = ResolveMediaPath is null ? item.LocalPath : await ResolveMediaPath(item, cancellation.Token);
            if (generation != _mediaGeneration) return;
            if (path is null) { MediaStatus = poster ? null : "Media unavailable. Check your connection and try again."; return; }
            if (item.IsVideo)
            {
                PlayingVideoPath = path;
            }
            else
            {
                var bitmap = await Task.Run(() =>
                {
                    return EmuShelf.App.Services.SafeImageDecoder.DecodeToFit(path, 1240, 1080);
                }, cancellation.Token);
                if (generation != _mediaGeneration) { bitmap.Dispose(); return; }
                MediaImage = bitmap;
            }
            MediaStatus = null;
        }
        catch (OperationCanceledException) { }
        catch (Exception)
        {
            if (generation == _mediaGeneration) MediaStatus = poster ? null : "This media could not be opened.";
        }
        finally
        {
            if (generation == _mediaGeneration) IsMediaLoading = false;
        }
    }

    public void StopMedia()
    {
        ++_mediaGeneration;
        _mediaLoad?.Cancel();
        _mediaLoad?.Dispose();
        _mediaLoad = null;
        PlayingVideoPath = null;
        MediaImage = null;
        MediaStatus = null;
        IsMediaLoading = false;
    }
}
