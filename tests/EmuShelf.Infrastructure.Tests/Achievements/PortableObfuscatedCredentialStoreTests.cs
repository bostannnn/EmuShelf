using System.Text;
using EmuShelf.Infrastructure.Achievements;

namespace EmuShelf.Infrastructure.Tests.Achievements;

public class PortableObfuscatedCredentialStoreTests : TempAppDirectoryTestBase
{
    private const string SampleKey = "AbCdEf1234567890ZyXwVu_9876";

    private string BlobPath =>
        Path.Combine(AppPaths.SettingsDirectory, RetroAchievementsCredentialStoreFactory.BlobFileName);

    private PortableObfuscatedCredentialStore CreateStore() => new(BlobPath);

    [Fact]
    public void SaveThenGet_RoundTripsTheKey()
    {
        var store = CreateStore();

        store.SaveApiKey(SampleKey);

        Assert.Equal(SampleKey, store.GetApiKey());
    }

    [Fact]
    public void GetApiKey_SurvivesAcrossInstances()
    {
        // A fresh instance models the next launch after an update: the key must persist to disk, not
        // just live in memory. This is the regression guard for the Linux/Steam Deck "forgets the key
        // after every update" report.
        CreateStore().SaveApiKey(SampleKey);

        var afterRestart = CreateStore().GetApiKey();

        Assert.Equal(SampleKey, afterRestart);
    }

    [Fact]
    public void SaveApiKey_ReplacesThePreviousKey()
    {
        var store = CreateStore();
        store.SaveApiKey(SampleKey);

        store.SaveApiKey("a-different-key");

        Assert.Equal("a-different-key", store.GetApiKey());
    }

    [Fact]
    public void ClearApiKey_RemovesTheStoredKeyAndFile()
    {
        var store = CreateStore();
        store.SaveApiKey(SampleKey);

        store.ClearApiKey();

        Assert.Null(store.GetApiKey());
        Assert.False(File.Exists(BlobPath));
    }

    [Fact]
    public void GetApiKey_WhenNothingStored_ReturnsNull()
    {
        Assert.Null(CreateStore().GetApiKey());
    }

    [Fact]
    public void StoredBlob_DoesNotContainThePlaintextKey()
    {
        CreateStore().SaveApiKey(SampleKey);

        var raw = File.ReadAllBytes(BlobPath);
        // Latin1 is a 1:1 byte->char map, so a literal ASCII key would show up verbatim if present.
        var asText = Encoding.Latin1.GetString(raw);
        Assert.DoesNotContain(SampleKey, asText, StringComparison.Ordinal);
    }

    [Fact]
    public void GetApiKey_WhenBlobIsCorrupt_ReturnsNullInsteadOfThrowing()
    {
        Directory.CreateDirectory(AppPaths.SettingsDirectory);
        File.WriteAllBytes(BlobPath, new byte[40]); // right length, wrong contents -> auth tag fails

        Assert.Null(CreateStore().GetApiKey());
    }

    [Fact]
    public void GetApiKey_WhenBlobTooShort_ReturnsNull()
    {
        Directory.CreateDirectory(AppPaths.SettingsDirectory);
        File.WriteAllBytes(BlobPath, new byte[] { 1, 2, 3 });

        Assert.Null(CreateStore().GetApiKey());
    }

    [Fact]
    public void SaveApiKey_RestrictsTheFileToOwnerOnUnix()
    {
        if (OperatingSystem.IsWindows())
            return; // Unix permission model only; NTFS ACLs govern the Windows store.

        CreateStore().SaveApiKey(SampleKey);

        Assert.Equal(UnixFileMode.UserRead | UnixFileMode.UserWrite, File.GetUnixFileMode(BlobPath));
    }
}
