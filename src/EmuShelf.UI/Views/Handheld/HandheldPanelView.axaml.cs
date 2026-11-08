using System.ComponentModel;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;
using EmuShelf.App.ViewModels;

namespace EmuShelf.App.Views.Handheld;

/// <summary>
/// A Handheld panel on the Launcher (M45 H6c). A pure view onto <see cref="HandheldPanelViewModel"/>; the only wiring here
/// keeps the focused row in view as the pad moves (the list virtualizes, so a long set of achievements stays cheap). Taps
/// are found by the Launcher's touch wiring and handed to the shell.
/// </summary>
public partial class HandheldPanelView : UserControl
{
    private readonly ItemsControl _rows;
    private HandheldPanelViewModel? _panel;

    public HandheldPanelView()
    {
        InitializeComponent();
        _rows = this.FindControl<ItemsControl>("PanelRows")!;
        var host = this.FindControl<Panel>("PanelScrollHost")!;
        var scroller = this.FindControl<ScrollViewer>("PanelScroller")!;
        // Every row is 100 px with a 12 px gap below it: the scroller shows whole rows, never one cut at an edge.
        host.SizeChanged += (_, _) => HandheldScrolling.FitWholeRows(scroller, host.Bounds.Height, RowPitch);
        HandheldScrolling.KeepOnWholeRows(scroller, () => RowPitch);
        DataContextChanged += (_, _) =>
        {
            if (_panel is { } old)
                old.PropertyChanged -= OnPanelChanged;
            _panel = DataContext as HandheldPanelViewModel;
            if (_panel is { } panel)
            {
                panel.PropertyChanged += OnPanelChanged;
                RevealSoon();
            }
        };
    }

    private void OnPanelChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(HandheldPanelViewModel.FocusIndex) or nameof(HandheldPanelViewModel.IsTyping))
            RevealSoon();
    }

    // After layout, so a rebuilt list has its containers.
    private void RevealSoon() => Dispatcher.UIThread.Post(Reveal, DispatcherPriority.Loaded);

    private void Reveal()
    {
        if (_panel is { FocusIndex: >= 0 and var index } panel && index < panel.Rows.Count && panel.IsListing)
            _rows.ScrollIntoView(index);
    }

    // A row's slot: its 100 px and the 12 px gap under it (HandheldPanelView.axaml).
    private const double RowPitch = 112;

    private void InitializeComponent() => AvaloniaXamlLoader.Load(this);
}
