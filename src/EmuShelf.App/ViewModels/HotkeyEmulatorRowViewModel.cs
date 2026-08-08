using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using EmuShelf.App.Services;
using EmuShelf.Core.Hotkeys;

namespace EmuShelf.App.ViewModels;

/// <summary>
/// One emulator's row in the Hotkeys settings section: the per-action grid, a status line, and the
/// Apply / Preview / Revert actions. All work is delegated to the <see cref="HotkeySettingsContext"/>,
/// so the row holds no emulator knowledge and stays testable with a fake context.
/// </summary>
public partial class HotkeyEmulatorRowViewModel : ObservableObject
{
    private readonly HotkeySettingsContext _context;

    public HotkeyEmulatorRowViewModel(HotkeyEmulatorSnapshot snapshot, HotkeySettingsContext context)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        ArgumentNullException.ThrowIfNull(context);
        _context = context;
        EmulatorId = snapshot.EmulatorId;
        DisplayName = snapshot.DisplayName;
        Populate(snapshot);
    }

    public string EmulatorId { get; }

    public string DisplayName { get; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Rewind))]
    [NotifyPropertyChangedFor(nameof(FastForward))]
    [NotifyPropertyChangedFor(nameof(SaveState))]
    [NotifyPropertyChangedFor(nameof(LoadState))]
    [NotifyPropertyChangedFor(nameof(CloseGame))]
    public partial IReadOnlyList<HotkeyActionLine> Actions { get; set; } = [];

    /// <summary>Per-action cells for the settings matrix; every action is always present in a snapshot.</summary>
    public HotkeyActionLine? Rewind => Cell(HotkeyAction.Rewind);
    public HotkeyActionLine? FastForward => Cell(HotkeyAction.FastForward);
    public HotkeyActionLine? SaveState => Cell(HotkeyAction.SaveState);
    public HotkeyActionLine? LoadState => Cell(HotkeyAction.LoadState);
    public HotkeyActionLine? CloseGame => Cell(HotkeyAction.CloseGame);

    private HotkeyActionLine? Cell(HotkeyAction action) =>
        Actions.FirstOrDefault(line => line.Action == action);

    [ObservableProperty]
    public partial string StatusText { get; set; } = string.Empty;

    [ObservableProperty]
    public partial HotkeyRowTone StatusTone { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanRun))]
    public partial bool CanOperate { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanRun))]
    public partial bool IsBusy { get; set; }

    /// <summary>Whether the action buttons are enabled: the config is resolvable and no op is in flight.</summary>
    public bool CanRun => CanOperate && !IsBusy;

    [RelayCommand]
    private Task Apply() => RunAsync(_context.ApplyAsync);

    [RelayCommand]
    private Task Preview() => RunAsync(_context.PreviewAsync);

    [RelayCommand]
    private Task Revert() => RunAsync(_context.RevertAsync);

    /// <summary>Runs one operation and folds its snapshot back in. Reused by the parent's Apply-all.</summary>
    internal async Task RunAsync(Func<string, CancellationToken, Task<HotkeyEmulatorSnapshot>> operation)
    {
        if (IsBusy)
            return;

        IsBusy = true;
        try
        {
            Populate(await operation(EmulatorId, CancellationToken.None));
        }
        finally
        {
            IsBusy = false;
        }
    }

    private void Populate(HotkeyEmulatorSnapshot snapshot)
    {
        Actions = snapshot.Actions;
        StatusText = snapshot.StatusText;
        StatusTone = snapshot.StatusTone;
        CanOperate = snapshot.CanOperate;
    }
}
