using System.ComponentModel;
using Avalonia.Controls;
using Avalonia.Threading;
using EmuShelf.App.ViewModels;

namespace EmuShelf.App.Views;

/// <summary>
/// The pre-boot setup page (see the markup). The only code is focus reveal: the view-model bumps
/// <see cref="SetupWizardViewModel.FocusRevision"/> and the focused row is scrolled into view, the way
/// the couch Settings overlay does for its rows.
/// </summary>
public partial class SetupWizardView : UserControl
{
    private SetupWizardViewModel? _viewModel;

    public SetupWizardView()
    {
        InitializeComponent();
        DataContextChanged += (_, _) => Attach(DataContext as SetupWizardViewModel);
    }

    private void Attach(SetupWizardViewModel? viewModel)
    {
        if (_viewModel is not null)
            _viewModel.PropertyChanged -= OnViewModelPropertyChanged;
        _viewModel = viewModel;
        if (_viewModel is not null)
            _viewModel.PropertyChanged += OnViewModelPropertyChanged;
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(SetupWizardViewModel.FocusRevision) or nameof(SetupWizardViewModel.FocusedRowIndex))
            Dispatcher.UIThread.Post(RevealFocusedRow, DispatcherPriority.Loaded);
    }

    private void RevealFocusedRow()
    {
        if (_viewModel is null || _viewModel.Rows.Count == 0)
            return;
        var index = Math.Clamp(_viewModel.FocusedRowIndex, 0, _viewModel.Rows.Count - 1);
        RowsScroller.UpdateLayout();
        RowsRepeater.UpdateLayout();
        var element = RowsRepeater.TryGetElement(index) ?? RowsRepeater.GetOrCreateElement(index);
        element?.BringIntoView();
    }
}
