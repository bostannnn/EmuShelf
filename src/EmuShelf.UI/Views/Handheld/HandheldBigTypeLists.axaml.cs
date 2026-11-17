using Avalonia;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using Avalonia.Media;

namespace EmuShelf.App.Views.Handheld;

/// <summary>
/// The Big Type look's lists on the Launcher (M47 B7): the Start menu, the Y sheet, Settings and a panel as type on the colour. A view
/// onto <see cref="HandheldListsShown"/> (the shell's list, kept while its rows leave); its rows' arrival and leaving, the grid stepping
/// back and the ring are the Launcher's motion's (<see cref="HandheldLauncherMotion"/>).
/// </summary>
public partial class HandheldBigTypeLists : UserControl
{
    public HandheldBigTypeLists()
    {
        InitializeComponent();
        MenuRows = Find<Control>("MenuRowsPart");
        MenuTitle = Find<HandheldBigTypeHeading>("MenuTitlePart");
        SheetRows = Find<Control>("SheetRowsPart");
        SheetTitle = Find<HandheldBigTypeHeading>("SheetTitlePart");
        Rail = Find<Control>("RailPart");
        SettingsRows = Find<Control>("SettingsRowsPart");
        SettingsTitle = Find<HandheldBigTypeHeading>("SettingsTitlePart");
        ChoiceRows = Find<Control>("ChoiceRowsPart");
        ChoiceTitle = Find<HandheldBigTypeHeading>("ChoiceTitlePart");
        ConfirmRows = Find<Control>("ConfirmRowsPart");
        ConfirmTitle = Find<HandheldBigTypeHeading>("ConfirmTitlePart");
        PanelRows = Find<Control>("PanelRowsPart");
        PanelTitle = Find<HandheldBigTypeHeading>("PanelTitlePart");
        PanelGrid = Find<Control>("PanelGridPart");
        KeyboardTitle = Find<HandheldBigTypeHeading>("KeyboardTitlePart");
        KeyboardKeys = Find<Control>("KeyboardKeysPart");
        KeyboardColours = Find<Control>("KeyboardColoursPart");
        KeyboardHits = Find<Control>("KeyboardHitsPart");
        // View wiring: a list's window clips its rows above and below only, so a row sliding in from the side is never cut.
        foreach (var window in this.GetLogicalDescendantsOfClass("rows-window"))
            // Its height is its most rows (MaxHeight), not what it holds now: rows streaming in (cover search's) grow it after the clip is set.
            window.SizeChanged += (_, e) => window.Clip = new RectangleGeometry(new Rect(-Spill, 0, e.NewSize.Width + (2 * Spill),
                double.IsFinite(window.MaxHeight) ? window.MaxHeight : e.NewSize.Height));
    }

    // How far a window lets its rows run out at the sides: the widest canvas (this view's own width is not known yet when its
    // windows are first sized; on the Thor that clip cut the rail's long names, M47 B7).
    private const double Spill = EmuShelf.App.ViewModels.HandheldShellViewModel.WideCanvasWidth;

    internal Control MenuRows { get; }
    internal HandheldBigTypeHeading MenuTitle { get; }
    internal Control SheetRows { get; }
    internal HandheldBigTypeHeading SheetTitle { get; }
    internal Control Rail { get; }
    internal Control SettingsRows { get; }
    internal HandheldBigTypeHeading SettingsTitle { get; }
    internal Control ChoiceRows { get; }
    internal HandheldBigTypeHeading ChoiceTitle { get; }
    internal Control ConfirmRows { get; }
    internal HandheldBigTypeHeading ConfirmTitle { get; }
    internal Control PanelRows { get; }
    internal HandheldBigTypeHeading PanelTitle { get; }
    internal Control PanelGrid { get; }
    internal HandheldBigTypeHeading KeyboardTitle { get; }
    internal Control KeyboardKeys { get; }
    internal Control KeyboardColours { get; }
    internal Control KeyboardHits { get; }

    private T Find<T>(string name) where T : Control =>
        this.FindControl<T>(name) ?? throw new System.InvalidOperationException($"The lists have no {name}.");

    private void InitializeComponent() => AvaloniaXamlLoader.Load(this);
}

internal static class HandheldBigTypeListsExtensions
{
    public static System.Collections.Generic.IEnumerable<Panel> GetLogicalDescendantsOfClass(this Control root, string className)
    {
        foreach (var child in Avalonia.LogicalTree.LogicalExtensions.GetLogicalDescendants(root))
        {
            if (child is Panel panel && panel.Classes.Contains(className))
                yield return panel;
        }
    }
}
