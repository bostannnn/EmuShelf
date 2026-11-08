using System.ComponentModel;
using Avalonia.Controls;
using Avalonia.VisualTree;
using EmuShelf.App.ViewModels;

namespace EmuShelf.App.Views;

/// <summary>The play log page. Code-behind only keeps the Gamepad ring's row in view.</summary>
public partial class ActivityView : UserControl
{
    private ActivityViewModel? _viewModel;

    public ActivityView()
    {
        InitializeComponent();
        DataContextChanged += (_, _) =>
        {
            if (_viewModel is not null)
                _viewModel.PropertyChanged -= OnViewModelPropertyChanged;
            _viewModel = DataContext as ActivityViewModel;
            if (_viewModel is not null)
                _viewModel.PropertyChanged += OnViewModelPropertyChanged;
        };
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(ActivityViewModel.FocusRevision) || _viewModel?.FocusedRow is not { } row)
            return;
        // After layout, so a freshly loaded page has its rows before one is brought into view.
        Avalonia.Threading.Dispatcher.UIThread.Post(() =>
            RowsScroller.GetVisualDescendants().OfType<Button>()
                .FirstOrDefault(button => ReferenceEquals(button.DataContext, row))
                ?.BringIntoView(), Avalonia.Threading.DispatcherPriority.Background);
    }
}
