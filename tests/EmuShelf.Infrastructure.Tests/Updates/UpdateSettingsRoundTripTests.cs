using EmuShelf.Core.Settings;
using EmuShelf.Infrastructure.Settings;

namespace EmuShelf.Infrastructure.Tests.Updates;

public class UpdateSettingsRoundTripTests : TempAppDirectoryTestBase
{
    public UpdateSettingsRoundTripTests() => AppPaths.EnsureDirectoriesExist();

    [Fact]
    public void Defaults_AutomaticCheckOn_NoSkipOrLastCheck()
    {
        var settings = new AppSettings();

        Assert.True(settings.Updates.AutomaticallyCheck);
        Assert.Null(settings.Updates.LastCheckUtc);
        Assert.Null(settings.Updates.SkippedVersion);
    }

    [Fact]
    public void SaveThenLoad_RoundTripsUpdateSettings()
    {
        var service = new JsonSettingsService(AppPaths);
        var lastCheck = new DateTimeOffset(2026, 8, 5, 9, 30, 0, TimeSpan.Zero);
        service.Save(new AppSettings
        {
            Updates = new UpdateSettings
            {
                AutomaticallyCheck = false,
                LastCheckUtc = lastCheck,
                SkippedVersion = "1.2.3",
            },
        });

        var loaded = service.Load();

        Assert.False(loaded.Updates.AutomaticallyCheck);
        Assert.Equal(lastCheck, loaded.Updates.LastCheckUtc);
        Assert.Equal("1.2.3", loaded.Updates.SkippedVersion);
    }
}
