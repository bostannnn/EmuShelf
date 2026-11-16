using Avalonia.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;

namespace EmuShelf.App.ViewModels;

/// <summary>
/// The idle artwork as the Porthole on the Stage (M47 B15, proposals-screens.html option 12, the owner's pick of 16 November 2026): while the
/// drift shows a recent game, its artwork sits in a round window beside its name over its console's backdrop, and the name's box ends
/// before the window. Big Type only; today's drift keeps its fan-art hero.
/// </summary>
public sealed partial class HandheldStageViewModel
{
    /// <summary>The drift's game's artwork in the round window; null shows none. Owned (and disposed) by the shell.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasPorthole), nameof(NameBoxWidth), nameof(NarrowNameBoxWidth))]
    public partial Bitmap? PortholeArt { get; set; }

    /// <summary>The round window shows: the name's box ends before it.</summary>
    public bool HasPorthole => PortholeArt is not null;
}
