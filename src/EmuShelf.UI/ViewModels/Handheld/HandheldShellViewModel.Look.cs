using CommunityToolkit.Mvvm.ComponentModel;
using EmuShelf.Core.Handheld;

namespace EmuShelf.App.ViewModels;

public sealed partial class HandheldShellViewModel
{
    /// <summary>The look selected for this handheld session.</summary>
    [ObservableProperty]
    public partial HandheldLook Look { get; set; } = HandheldLook.BigType;
}
