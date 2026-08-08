using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using EmuShelf.App.Services;
using EmuShelf.Core.Settings;

namespace EmuShelf.App.Tests;

public class WindowInterfaceModeServiceTests
{
    [AvaloniaFact]
    public async Task ShortcutOverrideWinsForOneRunWithoutChangingTheSavedDefault()
    {
        var settings = new MemorySettingsService
        {
            Current = new AppSettings { InterfaceMode = InterfaceMode.Gamepad },
        };
        var window = new Window();
        var service = new WindowInterfaceModeService(
            settings,
            settings.Current,
            window,
            InterfaceMode.Desktop);

        Assert.Equal(InterfaceMode.Desktop, service.Current);
        Assert.True(service.IsCommandLineOverride);

        await service.SetModeAsync(InterfaceMode.Gamepad);

        Assert.Equal(InterfaceMode.Gamepad, service.Current);
        Assert.Equal(InterfaceMode.Gamepad, settings.Current.InterfaceMode);
        Assert.Equal(0, settings.SaveCalls);
    }

    [AvaloniaFact]
    public async Task UnforcedModeChangePersistsAgainstTheLatestSettings()
    {
        var settings = new MemorySettingsService
        {
            Current = new AppSettings
            {
                Theme = ThemePreference.Dark,
                InterfaceMode = InterfaceMode.Desktop,
            },
        };
        var service = new WindowInterfaceModeService(
            settings,
            settings.Current,
            new Window(),
            interfaceModeOverride: null);

        await service.SetModeAsync(InterfaceMode.Gamepad);

        Assert.Equal(InterfaceMode.Gamepad, settings.Current.InterfaceMode);
        Assert.Equal(ThemePreference.Dark, settings.Current.Theme);
    }

    [AvaloniaFact]
    public async Task GamepadLaunch_FirstSwitchToDesktop_MaximizesInsteadOfAFloatingWindow()
    {
        // On a handheld/TV the app launches straight into Gamepad (full screen). There is no prior
        // desktop window to restore, so the first switch to Desktop must maximize rather than drop
        // to the small default floating window.
        var settings = new MemorySettingsService
        {
            Current = new AppSettings { InterfaceMode = InterfaceMode.Gamepad },
        };
        var window = new Window();
        var service = new WindowInterfaceModeService(
            settings,
            settings.Current,
            window,
            interfaceModeOverride: null);

        Assert.Equal(WindowState.FullScreen, window.WindowState);

        await service.SetModeAsync(InterfaceMode.Desktop);

        Assert.Equal(WindowState.Maximized, window.WindowState);
    }

    [AvaloniaFact]
    public async Task DesktopLaunch_RoundTripThroughGamepad_RestoresTheOriginalWindow()
    {
        // A desktop launch has a real window the user sized; a trip through Gamepad must bring back
        // exactly that state, not force-maximize it.
        var settings = new MemorySettingsService
        {
            Current = new AppSettings { InterfaceMode = InterfaceMode.Desktop },
        };
        var window = new Window { WindowState = WindowState.Normal };
        var service = new WindowInterfaceModeService(
            settings,
            settings.Current,
            window,
            interfaceModeOverride: null);

        Assert.Equal(WindowState.Normal, window.WindowState);

        await service.SetModeAsync(InterfaceMode.Gamepad);
        Assert.Equal(WindowState.FullScreen, window.WindowState);

        await service.SetModeAsync(InterfaceMode.Desktop);
        Assert.Equal(WindowState.Normal, window.WindowState);
    }

    [AvaloniaFact]
    public async Task GamepadModeHidesTheCursorAndDesktopRestoresIt()
    {
        // Gamepad is a controller/TV surface: hide the pointer so an accidental trackpad bump can't
        // park a visible cursor over the shell. Desktop must bring the normal arrow back.
        var settings = new MemorySettingsService
        {
            Current = new AppSettings { InterfaceMode = InterfaceMode.Gamepad },
        };
        var window = new Window();
        var service = new WindowInterfaceModeService(
            settings,
            settings.Current,
            window,
            interfaceModeOverride: null);

        var hidden = window.Cursor;
        Assert.NotNull(hidden);
        Assert.NotSame(Cursor.Default, hidden);

        await service.SetModeAsync(InterfaceMode.Desktop);
        Assert.Same(Cursor.Default, window.Cursor);

        // Back to Gamepad: the same shared hidden cursor is reused, not a fresh allocation per switch.
        await service.SetModeAsync(InterfaceMode.Gamepad);
        Assert.Same(hidden, window.Cursor);
    }

    private sealed class MemorySettingsService : ISettingsService
    {
        public AppSettings Current { get; set; } = new();
        public int SaveCalls { get; private set; }

        public AppSettings Load() => Current;

        public void Save(AppSettings settings)
        {
            Current = settings;
            SaveCalls++;
        }
    }
}
