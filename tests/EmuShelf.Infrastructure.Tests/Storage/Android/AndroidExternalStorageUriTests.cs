using EmuShelf.Core.Storage.Android;

namespace EmuShelf.Infrastructure.Tests.Storage.Android;

public class AndroidExternalStorageUriTests
{
    [Fact]
    public void BuildTreeDocumentUri_ReproducesTheUriMeasuredWorkingOnTheThor()
    {
        // The exact URI that launched Metal Gear Solid via DuckStation in Milestone 0b: tree scoped to
        // roms/psx (the folder DuckStation holds a persisted grant to), document the multi-disc .m3u.
        var uri = AndroidExternalStorageUri.BuildTreeDocumentUri(
            "AE6A-1092",
            "roms/psx",
            "roms/psx/Metal Gear Solid (USA) (Rev 1)/Metal Gear Solid (USA) (Rev 1).m3u");

        Assert.Equal(
            "content://com.android.externalstorage.documents/tree/AE6A-1092%3Aroms%2Fpsx/document/" +
            "AE6A-1092%3Aroms%2Fpsx%2FMetal%20Gear%20Solid%20(USA)%20(Rev%201)%2F" +
            "Metal%20Gear%20Solid%20(USA)%20(Rev%201).m3u",
            uri);
    }

    [Fact]
    public void TryResolveLocalPath_UsesTheDocumentIdWhenPresent_NotTheTree()
    {
        var uri = AndroidExternalStorageUri.BuildTreeDocumentUri(
            "AE6A-1092", "roms/psx", "roms/psx/Xenogears (USA)/Xenogears (USA).m3u");

        var resolved = AndroidExternalStorageUri.TryResolveLocalPath(uri);

        Assert.Equal("/storage/AE6A-1092/roms/psx/Xenogears (USA)/Xenogears (USA).m3u", resolved);
    }

    [Fact]
    public void TryResolveLocalPath_MapsPrimaryVolumeToEmulatedZero()
    {
        var resolved = AndroidExternalStorageUri.TryResolveLocalPath(
            "content://com.android.externalstorage.documents/tree/primary%3AEmuShelfRoms");

        Assert.Equal("/storage/emulated/0/EmuShelfRoms", resolved);
    }

    [Fact]
    public void TryResolveLocalPath_RejectsOtherProviders()
    {
        Assert.Null(AndroidExternalStorageUri.TryResolveLocalPath(
            "content://com.android.providers.media.documents/document/image%3A1000"));
    }

    [Fact]
    public void TryResolveLocalPath_RejectsAVolumeThatWouldEscapeStorage()
    {
        // A document id whose volume smuggles a separator ("..%2F..") must not resolve to a path outside
        // /storage, even though this translation runs with all-files access.
        Assert.Null(AndroidExternalStorageUri.TryResolveLocalPath(
            "content://com.android.externalstorage.documents/tree/..%2F..%2Fdata%3Asecrets"));
    }

    [Theory]
    [InlineData("content://com.android.externalstorage.documents/tree/..%3A")]
    [InlineData("content://com.android.externalstorage.documents/tree/.%3A")]
    public void TryResolveLocalPath_RejectsDotVolumes_EvenWithEmptyRelative(string uri)
    {
        // The empty-relative branch returns the volume root directly, so a "." / ".." volume must be
        // rejected up front or it would resolve to a parent of /storage.
        Assert.Null(AndroidExternalStorageUri.TryResolveLocalPath(uri));
    }

    [Theory]
    [InlineData("/storage/emulated/0/roms/psx/game.chd", "primary", "roms/psx/game.chd")]
    [InlineData("/storage/AE6A-1092/roms/ps2/game.chd", "AE6A-1092", "roms/ps2/game.chd")]
    [InlineData("/storage/emulated/0", "primary", "")]
    public void TrySplitLocalPath_SplitsStoragePathsIntoVolumeAndRelative(
        string path, string expectedVolume, string expectedRelative)
    {
        Assert.True(AndroidExternalStorageUri.TrySplitLocalPath(path, out var volume, out var relative));
        Assert.Equal(expectedVolume, volume);
        Assert.Equal(expectedRelative, relative);
    }

    [Theory]
    [InlineData("/data/data/com.emushelf.app/files/library.db")]
    [InlineData("/storage/self/primary/roms")]
    public void TrySplitLocalPath_RejectsNonAddressablePaths(string path)
    {
        Assert.False(AndroidExternalStorageUri.TrySplitLocalPath(path, out _, out _));
    }

    [Fact]
    public void BuildThenSplit_RoundTripsAnSdCardGame()
    {
        const string original = "/storage/AE6A-1092/roms/psx/Koudelka (USA)/Koudelka (USA).m3u";
        Assert.True(AndroidExternalStorageUri.TrySplitLocalPath(original, out var volume, out var relative));

        var uri = AndroidExternalStorageUri.BuildTreeDocumentUri(volume, "roms/psx", relative);

        Assert.Equal(original, AndroidExternalStorageUri.TryResolveLocalPath(uri));
    }
}
