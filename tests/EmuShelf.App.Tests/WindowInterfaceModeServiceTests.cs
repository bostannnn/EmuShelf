using Avalonia.Controls;
using Avalonia.Headless.XUnit;
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
