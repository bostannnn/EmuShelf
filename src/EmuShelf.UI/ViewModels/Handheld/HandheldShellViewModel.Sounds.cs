using System;
using EmuShelf.App.Services;
using EmuShelf.Core.Handheld;

namespace EmuShelf.App.ViewModels;

// The interface sounds (M45 H6d): move, open, close, confirm and back, original tones (HandheldSoundSynth) with an off
// switch in the Start menu and in Settings → Display. Rather than a call at every place something opens or moves, each
// press and each touch is one interaction: what was open and where the selector was are noted before it, compared after
// it, and the difference picks the sound. So a new screen gets its sounds by being a layer the shell already knows.
// The companion (a game running, or its layers over the Launcher) stays silent: no sound plays over a game.
public sealed partial class HandheldShellViewModel
{
    private IHandheldSoundPlayer _soundPlayer = SilentHandheldSoundPlayer.Instance;
    private int _soundDepth;
    private SoundState _soundBefore;
    private GamepadAction? _soundAction;
    private bool _soundBumped;
    private bool _soundsEnabled = true;

    /// <summary>What plays the sounds; the Android head sets it, elsewhere they are silent.</summary>
    public IHandheldSoundPlayer SoundPlayer
    {
        get => _soundPlayer;
        set => _soundPlayer = value ?? SilentHandheldSoundPlayer.Instance;
    }

    /// <summary>The off switch (the Start menu's Sounds row, Settings → Display). Remembered through <see cref="SaveSounds"/>.</summary>
    public bool SoundsEnabled
    {
        get => _soundsEnabled;
        set
        {
            if (!SetProperty(ref _soundsEnabled, value))
                return;
            _soundsMenuItem.Note = SoundsNote;
        }
    }

    /// <summary>Remembers the off switch for the next start; set by the app.</summary>
    public Action<bool>? SaveSounds { get; set; }

    private string SoundsNote => SoundsEnabled ? "On" : "Off";

    /// <summary>The Start menu's Sounds row: flips the switch and says so.</summary>
    internal void ToggleSounds()
    {
        SoundsEnabled = !SoundsEnabled;
        SaveSounds?.Invoke(SoundsEnabled);
        ShowNotice(SoundsEnabled ? "Sounds on" : "Sounds off");
    }

    private void PlaySound(HandheldSound sound)
    {
        if (SoundsEnabled && !_disposed)
            _soundPlayer.Play(sound);
    }

    /// <summary>What the sounds compare: which layers are open (by identity) and where the selector is.</summary>
    internal readonly record struct SoundState(object? Folder, object? Title, object? Sheet, bool Menu, object? Panel,
        object? Settings, object? Naming, bool Moving, bool Companion, string Selection, string Content);

    private SoundState CaptureSoundState()
    {
        var folder = OpenFolder;
        var title = TitlePage;
        var settingsKeys = Settings?.Keyboard;
        var panelKeys = Panel?.Keyboard;
        var naming = Naming;
        var selection = string.Join('|',
            Zone, HomeIndex, DockIndex, Home.CurrentPage?.Index, folder?.SelectedIndex, folder?.FocusedHeaderItem, MenuIndex,
            Sheet?.Index, title?.State, title?.ButtonIndex, title?.OptionIndex, title?.ScreenIndex, Panel?.FocusIndex,
            settingsKeys?.KeyRow, settingsKeys?.KeyColumn, panelKeys?.KeyRow, panelKeys?.KeyColumn, naming?.KeyRow,
            naming?.KeyColumn, naming?.FocusedColor, _move?.Target);
        // A section's live status line (a scan's progress) is not something a press did.
        var content = string.Join('|', Settings?.Card is { } card ? card with { Status = string.Empty } : null, settingsKeys?.Text, panelKeys?.Text, naming?.Text, naming?.ColorIndex,
            folder?.Sort, title?.State);
        var companion = CompanionOwnsPad || (_companion is { } model && model.Overlay != SecondScreenOverlayKind.None);
        return new SoundState(folder, title, Sheet, IsMenuOpen, Panel, Settings, naming, _move is not null, companion,
            selection, content);
    }

    /// <summary>Opens one interaction (a press, or a touch when <paramref name="action"/> is null); nested ones join it.</summary>
    private void BeginSoundScope(GamepadAction? action)
    {
        if (_soundDepth++ == 0)
        {
            _soundBefore = CaptureSoundState();
            _soundAction = action;
            _soundBumped = false;
        }
        else
        {
            // A touch that does what a button does (a tap on a sheet's scrim is B) sounds like that button.
            _soundAction ??= action;
        }
    }

    private void EndSoundScope()
    {
        if (--_soundDepth > 0 || _disposed)
            return;
        var before = _soundBefore;
        var after = CaptureSoundState();
        if (before.Companion || after.Companion)
            return;
        if (SoundFor(before, after, _soundAction, _soundBumped) is { } sound)
            PlaySound(sound);
    }

    /// <summary>A touch on the Launcher, as one interaction: the view wraps what the touch does in it.</summary>
    public IDisposable BeginTouch()
    {
        BeginSoundScope(null);
        return new SoundScope(this);
    }

    private sealed class SoundScope(HandheldShellViewModel shell) : IDisposable
    {
        private bool _ended;

        public void Dispose()
        {
            if (_ended)
                return;
            _ended = true;
            shell.EndSoundScope();
        }
    }

    /// <summary>
    /// The sound for one interaction. Something opening sounds open; a folder or a Title page closing sounds close; any
    /// other layer closing sounds back on B and confirm otherwise (a row chosen, Start saving Settings). With the same
    /// layers open, the selector moving sounds move (not when it ran into a wall), A sounds confirm, and B that stepped
    /// back sounds back. A touch is treated as A once it changed more than where the selector is.
    /// </summary>
    internal static HandheldSound? SoundFor(SoundState before, SoundState after, GamepadAction? action, bool bumped)
    {
        static bool Opened(object? was, object? now) => now is not null && !ReferenceEquals(was, now);
        static bool Closed(object? was, object? now) => was is not null && now is null;

        if (Opened(before.Folder, after.Folder) || Opened(before.Title, after.Title) || Opened(before.Sheet, after.Sheet) ||
            (!before.Menu && after.Menu) || Opened(before.Panel, after.Panel) || Opened(before.Settings, after.Settings) ||
            Opened(before.Naming, after.Naming) || (!before.Moving && after.Moving))
            return HandheldSound.Open;
        if (Closed(before.Folder, after.Folder) || Closed(before.Title, after.Title))
            return HandheldSound.Close;
        if (Closed(before.Sheet, after.Sheet) || (before.Menu && !after.Menu) || Closed(before.Panel, after.Panel) ||
            Closed(before.Settings, after.Settings) || Closed(before.Naming, after.Naming) || (before.Moving && !after.Moving))
            return action == GamepadAction.Cancel ? HandheldSound.Back : HandheldSound.Confirm;

        var selected = before.Selection != after.Selection;
        var changed = selected || before.Content != after.Content;
        return action switch
        {
            GamepadAction.NavigateUp or GamepadAction.NavigateDown or GamepadAction.NavigateLeft or GamepadAction.NavigateRight
                or GamepadAction.PreviousPlatform or GamepadAction.NextPlatform =>
                changed && !bumped ? HandheldSound.Move : null,
            GamepadAction.Confirm => HandheldSound.Confirm,
            GamepadAction.Cancel => changed ? HandheldSound.Back : null,
            null => before.Content != after.Content ? HandheldSound.Confirm : selected ? HandheldSound.Move : null,
            _ => changed ? HandheldSound.Confirm : null,
        };
    }
}
