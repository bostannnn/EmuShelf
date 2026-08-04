using System.Text;
using EmuShelf.Core.Metadata.ScreenScraper;
using EmuShelf.Infrastructure.Metadata.ScreenScraper;

namespace EmuShelf.Infrastructure.Tests.Metadata;

public class ScreenScraperCredentialStoreTests : TempAppDirectoryTestBase
{
    [Fact]
    public void SessionOnlyStore_RoundTripsAndClearsWithoutWritingAFile()
    {
        var store = new SessionOnlyScreenScraperCredentialStore();
        var credentials = new ScreenScraperUserCredentials("player", "secret");

        store.SaveCredentials(credentials);

        Assert.Equal(credentials, store.GetCredentials());
        Assert.False(Directory.Exists(AppPaths.SettingsDirectory));
        store.ClearCredentials();
        Assert.Null(store.GetCredentials());
    }

    [Fact]
    public void Factory_PersistsAProtectedBlob_AndNeverStoresPlaintext()
    {
        AppPaths.EnsureDirectoriesExist();
        var store = ScreenScraperCredentialStoreFactory.Create(AppPaths);
        var credentials = new ScreenScraperUserCredentials("fixture-player", "VERY-SECRET-PASSWORD");

        store.SaveCredentials(credentials);

        // Every platform now persists (DPAPI on Windows, an AES-GCM blob on Linux/Steam Deck & macOS),
        // so the login survives a restart instead of dropping to the old session-only behaviour.
        Assert.IsType<TextBackedScreenScraperCredentialStore>(store);
        Assert.Equal(credentials, store.GetCredentials());
        var blobPath = Path.Combine(
            AppPaths.SettingsDirectory,
            ScreenScraperCredentialStoreFactory.BlobFileName);
        Assert.True(File.Exists(blobPath));
        var blobText = Encoding.UTF8.GetString(File.ReadAllBytes(blobPath));
        Assert.DoesNotContain(credentials.Username, blobText);
        Assert.DoesNotContain(credentials.Password, blobText);
        Assert.False(File.Exists(AppPaths.SettingsFilePath));

        store.ClearCredentials();
        Assert.False(File.Exists(blobPath));
    }

    [Fact]
    public void Factory_ReloadsTheStoredLoginFromDiskAcrossInstances()
    {
        AppPaths.EnsureDirectoriesExist();
        var credentials = new ScreenScraperUserCredentials("deck-player", "another-secret");

        ScreenScraperCredentialStoreFactory.Create(AppPaths).SaveCredentials(credentials);

        // A fresh store (a new launch) must recover the login from the portable blob, not memory.
        var reloaded = ScreenScraperCredentialStoreFactory.Create(AppPaths).GetCredentials();
        Assert.Equal(credentials, reloaded);
    }
}
