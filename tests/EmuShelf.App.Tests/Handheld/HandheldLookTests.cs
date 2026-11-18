using EmuShelf.App.ViewModels;
using EmuShelf.Core.Handheld;

namespace EmuShelf.App.Tests.Handheld;

// M47 B0: the look switch on the shell. Nothing reads it yet; the app sets it from the settings (MainViewModelTests).
public partial class HandheldShellViewModelTests
{
    [Avalonia.Headless.XUnit.AvaloniaFact]
    public void Look_DefaultsToBigType_AndCanBeSet()
    {
        var shell = new HandheldShellViewModel(new FakeLibrary());
        Assert.Equal(HandheldLook.BigType, shell.Look);
        Assert.Equal(HandheldLook.Classic, default(HandheldLook));

        var changed = new List<string?>();
        shell.PropertyChanged += (_, e) => changed.Add(e.PropertyName);
        shell.Look = HandheldLook.Classic;

        Assert.Equal(HandheldLook.Classic, shell.Look);
        Assert.Contains(nameof(HandheldShellViewModel.Look), changed);
    }
}
