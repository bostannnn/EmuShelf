using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using EmuShelf.Core.Launching;

namespace EmuShelf.App.ViewModels;

/// <summary>
/// Which part of the Title page has the pad. The disc chooser, the screen chooser and the actions sheet
/// are states of the page rather than overlays on a launch that already started (spec decision 13);
/// Launching covers the pre-launch save sync and the emulator start.
/// </summary>
public enum HandheldTitleState
{
    Buttons,
    DiscChooser,
    ScreenChooser,
    Launching,
}

public enum HandheldTitleButtonKind
{
    Play,
    Disc,
    Achievements,
}

/// <summary>One of the Title page's big buttons: Play, Disc (multi-disc games), Achievements.</summary>
public sealed partial class HandheldTitleButtonViewModel(HandheldTitleButtonKind kind, string label, string? note)
    : ObservableObject
{
    public HandheldTitleButtonKind Kind { get; } = kind;
    public string Label { get; } = label;
    public string? Note { get; } = note;
    public bool HasNote => !string.IsNullOrEmpty(Note);
    public bool IsPlay => Kind == HandheldTitleButtonKind.Play;
    public bool IsDisc => Kind == HandheldTitleButtonKind.Disc;
    public bool IsAchievements => Kind == HandheldTitleButtonKind.Achievements;

    [ObservableProperty]
    public partial bool IsFocused { get; set; }
}

/// <summary>A row in the disc chooser or the actions sheet.</summary>
public sealed partial class HandheldTitleOptionViewModel(string label, string? note, Action run) : ObservableObject
{
    public string Label { get; } = label;
    public string? Note { get; } = note;
    public bool HasNote => !string.IsNullOrEmpty(Note);
    internal Action Run { get; } = run;

    [ObservableProperty]
    public partial bool IsFocused { get; set; }
}

/// <summary>
/// A game's Title page on the Launcher (spec decision 13): what you are about to play, then Play. Owns its
/// cover bitmap (disposed by the shell when the page closes).
/// </summary>
public sealed partial class HandheldTitlePageViewModel : ObservableObject
{
    /// <summary>The screen chooser's three stops: the top-screen card, the bottom-screen card, the remember toggle.</summary>
    public const int ScreenTopIndex = 0;
    public const int ScreenBottomIndex = 1;
    public const int ScreenRememberIndex = 2;

    private readonly DateTimeOffset _now;

    public HandheldTitlePageViewModel(HandheldGameEntry entry, string crumb, string platformName, DateTimeOffset now)
    {
        _now = now;
        Entry = entry;
        Crumb = crumb;
        PlatformName = platformName;
    }

    public HandheldGameEntry Entry { get; }

    /// <summary>The platform's colour, a soft wash at the top of the page.</summary>
    public IBrush AccentBrush { get; init; } = Brushes.Gray;

    public string Title => Entry.Title;
    public string Crumb { get; }

    /// <summary>Where the page was opened from: "Home · PlayStation 2", or just the platform folder's name.</summary>
    public string CrumbLine => string.Equals(Crumb, PlatformName, StringComparison.Ordinal) ? Crumb : $"{Crumb} · {PlatformName}";
    public string PlatformName { get; }

    /// <summary>Null until the details read lands; the page shows the folder's facts meanwhile.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(PlatformLine))]
    [NotifyPropertyChangedFor(nameof(ScreenText))]
    [NotifyPropertyChangedFor(nameof(DiscText))]
    [NotifyPropertyChangedFor(nameof(HasDiscs))]
    public partial HandheldTitleDetails? Details { get; set; }

    public string PlatformLine => Details?.EmulatorName is { Length: > 0 } emulator
        ? $"{PlatformName} · {emulator}"
        : PlatformName;

    public string LastPlayedText => HandheldText.LastPlayed(Entry.LastPlayedAt, _now);

    public string PlaytimeText => Entry.Playtime.TotalMinutes >= 1
        ? HandheldText.Duration(Entry.Playtime)
        : Entry.PlayCount > 0 ? "Under a minute" : "None yet";

    public string ScreenText => Details is { } details ? HandheldText.RunsOn(details) : string.Empty;

    public bool HasDiscs => Details?.IsMultiDisc == true;

    public string? DiscText => Details is { IsMultiDisc: true } details
        ? $"{details.DiscNumbers.Count} · starts on disc {details.SelectedDiscNumber}"
        : null;

    public bool HasAchievements => !string.IsNullOrEmpty(Entry.AchievementText);

    /// <summary>The sheet's Play opened this page: it plays as soon as its details are in (M45 H6c).</summary>
    internal bool PlayWhenReady { get; set; }
    public string AchievementLine => $"{Entry.AchievementText} achievements";
    public double AchievementRatio => Entry.AchievementRatio;

    /// <summary>The cover, decoded at the page's size. Owned by the shell.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasCover))]
    public partial Bitmap? Cover { get; set; }

    public bool HasCover => Cover is not null;

    public ObservableCollection<HandheldTitleButtonViewModel> Buttons { get; } = [];

    /// <summary>The rows of whichever list state is open: the disc chooser or the actions sheet.</summary>
    public ObservableCollection<HandheldTitleOptionViewModel> Options { get; } = [];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsButtons))]
    [NotifyPropertyChangedFor(nameof(IsDiscChooser))]
    [NotifyPropertyChangedFor(nameof(IsScreenChooser))]
    [NotifyPropertyChangedFor(nameof(IsOptionList))]
    [NotifyPropertyChangedFor(nameof(IsLaunching))]
    [NotifyPropertyChangedFor(nameof(IsBrowsable))]
    [NotifyPropertyChangedFor(nameof(OptionsHeading))]
    public partial HandheldTitleState State { get; set; }

    public bool IsButtons => State == HandheldTitleState.Buttons;
    public bool IsDiscChooser => State == HandheldTitleState.DiscChooser;
    public bool IsScreenChooser => State == HandheldTitleState.ScreenChooser;
    // The actions sheet (Y) is the Launcher's game sheet since M45 H6c; the disc chooser is the page's one list state.
    public bool IsOptionList => State == HandheldTitleState.DiscChooser;
    public bool IsLaunching => State == HandheldTitleState.Launching;

    /// <summary>The facts and buttons are on screen (everything but the launching panel).</summary>
    public bool IsBrowsable => State != HandheldTitleState.Launching;

    public string OptionsHeading => "Which disc?";

    [ObservableProperty]
    public partial int ButtonIndex { get; set; }

    [ObservableProperty]
    public partial int OptionIndex { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsTopScreenFocused))]
    [NotifyPropertyChangedFor(nameof(IsBottomScreenFocused))]
    [NotifyPropertyChangedFor(nameof(IsRememberFocused))]
    public partial int ScreenIndex { get; set; }

    public bool IsTopScreenFocused => ScreenIndex == ScreenTopIndex;
    public bool IsBottomScreenFocused => ScreenIndex == ScreenBottomIndex;
    public bool IsRememberFocused => ScreenIndex == ScreenRememberIndex;

    /// <summary>"Always use this screen for …" — reset every time the chooser opens.</summary>
    [ObservableProperty]
    public partial bool RememberScreen { get; set; }

    public string RememberScreenLabel => $"Always use this screen for {PlatformName}";

    /// <summary>The pre-launch save sync is running; the launching panel says so (the blocking sync panel).</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(LaunchingHeading))]
    public partial bool IsSyncingSaves { get; set; }

    public string LaunchingHeading => IsSyncingSaves ? "Syncing saves…" : "Starting…";

    public string LaunchingDetail => Details?.EmulatorName is { Length: > 0 } emulator
        ? $"{Title} · {emulator}"
        : Title;

    internal void FocusButton(int index)
    {
        ButtonIndex = index;
        for (var i = 0; i < Buttons.Count; i++)
            Buttons[i].IsFocused = i == index;
    }

    internal void FocusOption(int index)
    {
        OptionIndex = index;
        for (var i = 0; i < Options.Count; i++)
            Options[i].IsFocused = i == index;
    }

    internal HandheldTitleButtonViewModel? FocusedButton =>
        ButtonIndex >= 0 && ButtonIndex < Buttons.Count ? Buttons[ButtonIndex] : null;

    internal int IndexOf(HandheldTitleButtonKind kind)
    {
        for (var i = 0; i < Buttons.Count; i++)
        {
            if (Buttons[i].Kind == kind)
                return i;
        }
        return -1;
    }
}

internal static partial class HandheldText
{
    public static string Duration(TimeSpan playtime) => playtime.TotalHours >= 1
        ? $"{(int)playtime.TotalHours} h {playtime.Minutes} min"
        : $"{Math.Max(1, (int)playtime.TotalMinutes)} min";

    /// <summary>"Today", "Yesterday", "3 days ago", or the date; "Not yet" for a game never played.</summary>
    public static string LastPlayed(DateTimeOffset? lastPlayed, DateTimeOffset now)
    {
        if (lastPlayed is not { } played)
            return "Not yet";
        var days = (now.ToLocalTime().Date - played.ToLocalTime().Date).Days;
        return days switch
        {
            <= 0 => "Today",
            1 => "Yesterday",
            < 7 => $"{days} days ago",
            _ => played.ToLocalTime().ToString("d MMM yyyy", CultureInfo.CurrentCulture),
        };
    }

    // The Thor's built-in display is the top panel and the presentation display the bottom one; Handheld
    // mode only runs there, so the Title page names them by position, as the mockup does.
    public const string TopScreen = "Top screen";
    public const string BottomScreen = "Bottom screen";

    public static string RunsOn(HandheldTitleDetails details)
    {
        if (!details.HasSecondScreen)
            return "This screen";
        // A DS/3DS always starts on the built-in screen (LaunchScreenResolver); its emulator lays out both
        // console screens itself.
        if (details.IsDualScreenSystem)
            return TopScreen;
        return details.Screen switch
        {
            LaunchScreenDecision.External => BottomScreen,
            LaunchScreenDecision.Prompt => "Asks which screen",
            _ => TopScreen,
        };
    }
}
