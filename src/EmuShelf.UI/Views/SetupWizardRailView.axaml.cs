using Avalonia;
using Avalonia.Controls;
using Avalonia.VisualTree;
using EmuShelf.App.ViewModels;

namespace EmuShelf.App.Views;

/// <summary>The setup wizard's step rail, keeping the selected step above the fixed Continue/Finish action.</summary>
public partial class SetupWizardRailView : UserControl
{
    private Button? _revealedStep;
    private Size _viewportSize;

    public SetupWizardRailView()
    {
        InitializeComponent();
        LayoutUpdated += (_, _) => RevealCurrentStep();
    }

    private void RevealCurrentStep()
    {
        var current = StepsScroller.GetVisualDescendants().OfType<Button>()
            .FirstOrDefault(button => button.DataContext is SetupStepViewModel { IsCurrent: true });
        if (current is null || (ReferenceEquals(current, _revealedStep) && _viewportSize == StepsScroller.Bounds.Size))
            return;

        _revealedStep = current;
        _viewportSize = StepsScroller.Bounds.Size;
        current.BringIntoView();
    }
}
