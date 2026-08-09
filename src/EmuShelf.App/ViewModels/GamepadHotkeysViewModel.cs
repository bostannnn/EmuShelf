using System.ComponentModel;
using System.Windows.Input;
using CommunityToolkit.Mvvm.ComponentModel;

namespace EmuShelf.App.ViewModels;

/// <summary>Which controller-focusable element in the Hotkeys overlay currently holds the ring.</summary>
public enum GamepadHotkeyTargetKind
{
    None,
    ApplyAll,
    InstallSteam,
    EmulatorApply,
    EmulatorRevert,
}

/// <summary>One line of the "hold Select + a face button" controller mapping shown in the overlay.</summary>
public sealed record GamepadHotkeyMappingRow(string Combo, string Action);

/// <summary>
/// Controller-native presentation and D-pad focus around the existing hotkey feature on
/// <see cref="EmulatorSettingsViewModel"/>. It owns no hotkey logic: it reuses that view model's
/// per-emulator rows (<see cref="EmulatorSettingsViewModel.HotkeyEmulators"/>), Apply-to-all and
/// Install-Steam-template commands, scheme summary, Steam status, and busy flag verbatim — exactly as
/// <see cref="GamepadScraperViewModel"/> wraps the shared scraper — and adds only the linear focus
/// model (D-pad Up/Down moves; A activates) the Gamepad overlay needs. This is Gamepad-native, not a
/// Desktop hand-off: no emulator settings window is ever opened.
/// </summary>
public sealed partial class GamepadHotkeysViewModel : ObservableObject, IDisposable
{
    private sealed class FocusTarget
    {
        public required GamepadHotkeyTargetKind Kind { get; init; }
        public HotkeyEmulatorRowViewModel? Row { get; init; }
        public required Action<bool> SetFocused { get; init; }
        public required Action Activate { get; init; }
    }

    private readonly EmulatorSettingsViewModel _settings;
    private readonly List<FocusTarget> _targets = [];
    private bool _disposed;

    public GamepadHotkeysViewModel(EmulatorSettingsViewModel settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        _settings = settings;
        _settings.PropertyChanged += OnSettingsPropertyChanged;
        RebuildTargets();
        SetFocus(0);
    }

    /// <summary>The per-emulator matrix rows, reused verbatim from the settings view model.</summary>
    public IReadOnlyList<HotkeyEmulatorRowViewModel> Emulators => _settings.HotkeyEmulators;

    public bool HasEmulators => _settings.HotkeyEmulators.Count > 0;

    /// <summary>A human summary of the keyboard scheme, shown at the top of the overlay.</summary>
    public string SchemeSummary => _settings.HotkeySchemeSummary;

    /// <summary>The "hold Select + a face button" mapping, so a controller-only user can set up the
    /// Steam Input layout that drives the scheme's keys. Identical to the Desktop legend.</summary>
    public IReadOnlyList<GamepadHotkeyMappingRow> ControllerMapping { get; } =
    [
        new("Select + Square", "Rewind (R)"),
        new("Select + Circle", "Fast-forward (L)"),
        new("Select + Triangle", "Save state (F2)"),
        new("Select + Cross", "Load state (F4)"),
        new("Select + Start", "Close game (F8)"),
    ];

    /// <summary>Last Steam-template install result message; empty until Install is used.</summary>
    public string SteamTemplateStatus => _settings.SteamTemplateStatus;

    public bool HasSteamTemplateStatus => !string.IsNullOrWhiteSpace(_settings.SteamTemplateStatus);

    /// <summary>True while the Apply-to-all pass runs, so the button can show it is working.</summary>
    public bool IsBusy => _settings.IsHotkeyBusy;

    /// <summary>Applies the recommended scheme to every configured emulator — the exact Desktop
    /// command, reused so pointer clicks and the D-pad A press run identical logic.</summary>
    public ICommand ApplyAllCommand => _settings.ApplyAllHotkeysCommand;

    /// <summary>Installs the bundled Steam Input layout — the exact Desktop command, reused.</summary>
    public ICommand InstallSteamTemplateCommand => _settings.InstallSteamTemplateCommand;

    public int FocusIndex { get; private set; } = -1;

    public GamepadHotkeyTargetKind FocusedKind =>
        FocusIndex >= 0 && FocusIndex < _targets.Count ? _targets[FocusIndex].Kind : GamepadHotkeyTargetKind.None;

    /// <summary>The emulator row under the focus ring, for the view and tests; null on the global targets.</summary>
    public HotkeyEmulatorRowViewModel? FocusedRow =>
        FocusIndex >= 0 && FocusIndex < _targets.Count ? _targets[FocusIndex].Row : null;

    // Scalar focus flags for the two global actions above the matrix. Per-emulator focus lives on the
    // rows themselves (HotkeyEmulatorRowViewModel.IsApplyFocused / IsRevertFocused).
    [ObservableProperty] public partial bool IsApplyAllFocused { get; set; }
    [ObservableProperty] public partial bool IsInstallFocused { get; set; }

    /// <summary>D-pad Up/Down: move the focus ring one step through the target list.</summary>
    public void MoveFocus(int delta) => SetFocus(FocusIndex + delta);

    /// <summary>A: activate the focused target — Apply-to-all, Install Steam template, or a row's
    /// Apply / Revert. Each underlying command guards itself (busy / not-operable), so activation is
    /// always safe even while an operation is in flight.</summary>
    public void Activate()
    {
        if (FocusIndex >= 0 && FocusIndex < _targets.Count)
            _targets[FocusIndex].Activate();
    }

    private void OnSettingsPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        switch (e.PropertyName)
        {
            case nameof(EmulatorSettingsViewModel.SteamTemplateStatus):
                OnPropertyChanged(nameof(SteamTemplateStatus));
                OnPropertyChanged(nameof(HasSteamTemplateStatus));
                break;
            case nameof(EmulatorSettingsViewModel.IsHotkeyBusy):
                OnPropertyChanged(nameof(IsBusy));
                break;
        }
    }

    private void RebuildTargets()
    {
        foreach (var target in _targets)
            target.SetFocused(false);
        _targets.Clear();
        FocusIndex = -1;

        _targets.Add(new FocusTarget
        {
            Kind = GamepadHotkeyTargetKind.ApplyAll,
            SetFocused = value => IsApplyAllFocused = value,
            Activate = () => Execute(_settings.ApplyAllHotkeysCommand),
        });
        _targets.Add(new FocusTarget
        {
            Kind = GamepadHotkeyTargetKind.InstallSteam,
            SetFocused = value => IsInstallFocused = value,
            Activate = () => Execute(_settings.InstallSteamTemplateCommand),
        });

        foreach (var row in _settings.HotkeyEmulators)
        {
            // A row whose config directory can't be resolved presents in the matrix (its cells and
            // status still read), but offers no focusable Apply / Revert — mirroring the Desktop
            // buttons being disabled there.
            if (!row.CanOperate)
                continue;

            var current = row;
            _targets.Add(new FocusTarget
            {
                Kind = GamepadHotkeyTargetKind.EmulatorApply,
                Row = current,
                SetFocused = value => current.IsApplyFocused = value,
                Activate = () => Execute(current.ApplyCommand),
            });
            _targets.Add(new FocusTarget
            {
                Kind = GamepadHotkeyTargetKind.EmulatorRevert,
                Row = current,
                SetFocused = value => current.IsRevertFocused = value,
                Activate = () => Execute(current.RevertCommand),
            });
        }
    }

    private void SetFocus(int index)
    {
        if (FocusIndex >= 0 && FocusIndex < _targets.Count)
            _targets[FocusIndex].SetFocused(false);

        FocusIndex = _targets.Count == 0 ? -1 : Math.Clamp(index, 0, _targets.Count - 1);

        if (FocusIndex >= 0)
            _targets[FocusIndex].SetFocused(true);

        OnPropertyChanged(nameof(FocusIndex));
        OnPropertyChanged(nameof(FocusedKind));
        OnPropertyChanged(nameof(FocusedRow));
    }

    private static void Execute(ICommand command, object? parameter = null)
    {
        if (command.CanExecute(parameter))
            command.Execute(parameter);
    }

    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;

        _settings.PropertyChanged -= OnSettingsPropertyChanged;
        // Clear the ring off the shared rows so a later reopen (or the Desktop matrix) starts clean.
        foreach (var target in _targets)
            target.SetFocused(false);
        _targets.Clear();
    }
}
