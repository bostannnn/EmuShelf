using CommunityToolkit.Mvvm.ComponentModel;
using EmuShelf.Core.Handheld;

namespace EmuShelf.App.ViewModels;

// M47 B0: the look switch the Big Type restyle is built behind. The app sets it from AppSettings.HandheldLook when Handheld
// mode starts; nothing reads it yet and Settings has no row for it.
public sealed partial class HandheldShellViewModel
{
    /// <summary>Which look Handheld mode draws: today's (the default) or Big Type, while M47 is built.</summary>
    [ObservableProperty]
    public partial HandheldLook Look { get; set; } = HandheldLook.Classic;
}
