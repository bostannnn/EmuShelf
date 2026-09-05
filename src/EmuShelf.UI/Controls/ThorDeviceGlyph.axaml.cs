using Avalonia;
using Avalonia.Controls;

namespace EmuShelf.App.Controls;

/// <summary>
/// A front view of the AYN Thor clamshell with either screen lit — the picture the couch launch-screen
/// chooser shows in place of describing "the top screen" in words. The drawing itself lives in
/// ThorDeviceGlyph.axaml; this half only forwards the two "which panel is on" flags into the <c>lit</c>
/// class on the matching screen and its halo, so the styling stays in markup.
/// </summary>
public partial class ThorDeviceGlyph : UserControl
{
    public static readonly StyledProperty<bool> IsTopLitProperty =
        AvaloniaProperty.Register<ThorDeviceGlyph, bool>(nameof(IsTopLit));

    public static readonly StyledProperty<bool> IsBottomLitProperty =
        AvaloniaProperty.Register<ThorDeviceGlyph, bool>(nameof(IsBottomLit));

    public ThorDeviceGlyph()
    {
        InitializeComponent();
        ApplyLit();
    }

    /// <summary>Whether the 6" lid screen is drawn as the active panel.</summary>
    public bool IsTopLit
    {
        get => GetValue(IsTopLitProperty);
        set => SetValue(IsTopLitProperty, value);
    }

    /// <summary>Whether the 3.92" base touch screen is drawn as the active panel.</summary>
    public bool IsBottomLit
    {
        get => GetValue(IsBottomLitProperty);
        set => SetValue(IsBottomLitProperty, value);
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == IsTopLitProperty || change.Property == IsBottomLitProperty)
            ApplyLit();
    }

    private void ApplyLit()
    {
        TopScreen.Classes.Set("lit", IsTopLit);
        TopHalo.Classes.Set("lit", IsTopLit);
        BottomScreen.Classes.Set("lit", IsBottomLit);
        BottomHalo.Classes.Set("lit", IsBottomLit);
    }
}
