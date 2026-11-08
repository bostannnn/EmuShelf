using EmuShelf.Infrastructure.Settings;

namespace EmuShelf.Infrastructure.Tests.Settings;

public class FileDeviceIdentityTests : TempAppDirectoryTestBase
{
    private string FilePath => Path.Combine(BaseDirectory, "Settings", "device.json");

    [Fact]
    public void CreatesAnId_OnFirstUse_AndKeepsItAcrossInstances()
    {
        var first = new FileDeviceIdentity(FilePath).DeviceId;

        Assert.False(string.IsNullOrWhiteSpace(first));
        Assert.True(File.Exists(FilePath));
        Assert.Equal(first, new FileDeviceIdentity(FilePath).DeviceId);
    }

    [Fact]
    public void ADamagedFile_GetsAFreshId_ThatIsThenKept()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
        File.WriteAllText(FilePath, "{ not json");

        var fresh = new FileDeviceIdentity(FilePath).DeviceId;

        Assert.False(string.IsNullOrWhiteSpace(fresh));
        Assert.Equal(fresh, new FileDeviceIdentity(FilePath).DeviceId);
    }
}
