using System.Collections.Generic;
using Avalonia.Controls;
using Avalonia.Interactivity;
using EmuShelf.Core.Systems;

namespace EmuShelf.App.Views;

public partial class SystemPickerWindow : Window
{
    public SystemPickerWindow()
    {
        InitializeComponent();
        AddButton.Click += OnAdd;
        CancelButton.Click += OnCancel;
    }

    public SystemPickerWindow(IReadOnlyList<GameSystem> systems, GameSystem? suggested) : this()
    {
        SystemCombo.ItemsSource = systems;
        SystemCombo.SelectedItem = suggested ?? (systems.Count > 0 ? systems[0] : null);
    }

    private void OnAdd(object? sender, RoutedEventArgs e) => Close(SystemCombo.SelectedItem as GameSystem);

    private void OnCancel(object? sender, RoutedEventArgs e) => Close(null);
}
