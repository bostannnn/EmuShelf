using EmuShelf.Core.Diagnostics;
using EmuShelf.Core.Settings;
using EmuShelf.Infrastructure.Settings;

namespace EmuShelf.Infrastructure.Tests.SaveSync;

public sealed class CloudSaveSyncSettingsTests : TempAppDirectoryTestBase
{
    [Fact]
    public void CloudSaveSyncSettings_RoundTripThroughPortableSettingsFile()
    {
        AppPaths.EnsureDirectoriesExist();
        var service = new JsonSettingsService(AppPaths, NullAppLogger.Instance);
        var settings = new AppSettings
        {
            CloudSaveSync = new CloudSaveSyncSettings
            {
                Enabled = true,
                RemoteName = "emushelf-gdrive",
                CloudFolder = "EmuShelf/Saves",
                Pcsx2ConfigDirectory = "/home/deck/pcsx2",
                PpssppMemoryStickDirectory = "/home/deck/Emulation/saves/ppsspp",
            },
        };

        service.Save(settings);

        Assert.Equal(settings.CloudSaveSync, service.Load().CloudSaveSync);
    }

    [Fact]
    public void CloudSaveSyncSettings_DefaultsToDisabledWhenAbsentFromFile()
    {
        var loaded = new JsonSettingsService(AppPaths, NullAppLogger.Instance).Load();

        Assert.False(loaded.CloudSaveSync.Enabled);
        Assert.Null(loaded.CloudSaveSync.RemoteName);
    }
}
