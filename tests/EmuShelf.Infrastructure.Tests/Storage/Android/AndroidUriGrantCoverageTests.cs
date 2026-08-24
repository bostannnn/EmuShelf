using EmuShelf.Core.Storage.Android;

namespace EmuShelf.Infrastructure.Tests.Storage.Android;

public class AndroidUriGrantCoverageTests
{
    // The tree grant EmuShelf would hold to the whole 3DS library folder on the Thor's SD card.
    private const string Roms3dsGrant =
        "content://com.android.externalstorage.documents/tree/AE6A-1092%3Aroms%2F3ds";

    // A single-file 3DS launch URI (Azahar) beneath that folder.
    private static string Game3dsUri(string file) =>
        "content://com.android.externalstorage.documents/tree/AE6A-1092%3Aroms%2F3ds/document/" +
        $"AE6A-1092%3Aroms%2F3ds%2F{file}";

    [Fact]
    public void GrantToLibraryFolder_CoversASingleFileBeneathIt()
    {
        Assert.True(AndroidUriGrantCoverage.IsCovered(
            [Roms3dsGrant], Game3dsUri("Mario%20Kart%207.3ds")));
    }

    [Fact]
    public void GrantToLibraryFolder_CoversANestedMultiDiscDocument()
    {
        // The launch URI's document can sit in a per-game sub-folder; a grant to the library root still
        // covers it (directory containment), which is what lets one grant serve every game in the folder.
        var nested =
            "content://com.android.externalstorage.documents/tree/AE6A-1092%3Aroms%2F3ds/document/" +
            "AE6A-1092%3Aroms%2F3ds%2FSome%20Game%2FSome%20Game.3ds";
        Assert.True(AndroidUriGrantCoverage.IsCovered([Roms3dsGrant], nested));
    }

    [Fact]
    public void GrantToTheGameFolderItself_Covers()
    {
        // The tree segment of a single-file launch equals the library folder; a grant to exactly that folder
        // still resolves as ancestor-or-self.
        Assert.True(AndroidUriGrantCoverage.IsCovered(
            [Roms3dsGrant], Game3dsUri("Game.cia")));
    }

    [Fact]
    public void GrantToADifferentVolume_DoesNotCover()
    {
        const string primaryGrant =
            "content://com.android.externalstorage.documents/tree/primary%3Aroms%2F3ds";
        Assert.False(AndroidUriGrantCoverage.IsCovered(
            [primaryGrant], Game3dsUri("Game.3ds")));
    }

    [Fact]
    public void GrantToASiblingFolder_DoesNotCoverViaStringPrefix()
    {
        // "roms/3ds" must not be treated as covering "roms/3ds-backup" — containment is segment-exact.
        const string backupGrant =
            "content://com.android.externalstorage.documents/tree/AE6A-1092%3Aroms%2F3ds-backup";
        Assert.False(AndroidUriGrantCoverage.IsCovered(
            [backupGrant], Game3dsUri("Game.3ds")));
    }

    [Fact]
    public void GrantToADeeperFolder_DoesNotCoverAShallowerGame()
    {
        const string deeperGrant =
            "content://com.android.externalstorage.documents/tree/AE6A-1092%3Aroms%2F3ds%2FRegion";
        Assert.False(AndroidUriGrantCoverage.IsCovered(
            [deeperGrant], Game3dsUri("Game.3ds")));
    }

    [Fact]
    public void FindCoveringGrant_ReturnsTheMatchingGrant_AmongMany()
    {
        const string psxGrant =
            "content://com.android.externalstorage.documents/tree/AE6A-1092%3Aroms%2Fpsx";
        var match = AndroidUriGrantCoverage.FindCoveringGrant(
            [psxGrant, Roms3dsGrant], Game3dsUri("Game.3ds"));
        Assert.Equal(Roms3dsGrant, match);
    }

    [Fact]
    public void NonExternalStorageOrNullTarget_IsNeverCovered()
    {
        Assert.False(AndroidUriGrantCoverage.IsCovered([Roms3dsGrant], null));
        Assert.False(AndroidUriGrantCoverage.IsCovered([Roms3dsGrant], "content://media/external/images/1"));
        Assert.False(AndroidUriGrantCoverage.IsCovered([Roms3dsGrant], "/storage/AE6A-1092/roms/3ds/Game.3ds"));
    }

    [Fact]
    public void NoHeldGrants_IsNeverCovered()
    {
        Assert.False(AndroidUriGrantCoverage.IsCovered([], Game3dsUri("Game.3ds")));
    }
}
