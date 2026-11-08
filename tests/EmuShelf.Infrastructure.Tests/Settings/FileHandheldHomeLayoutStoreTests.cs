using EmuShelf.Core.Handheld;
using EmuShelf.Infrastructure.Settings;

namespace EmuShelf.Infrastructure.Tests.Settings;

public sealed class FileHandheldHomeLayoutStoreTests : IDisposable
{
    private readonly string _directory = Path.Combine(
        Path.GetTempPath(), "EmuShelfHandheldHome", Guid.NewGuid().ToString("N"));

    private string SettingsDirectory => Path.Combine(_directory, "Settings");
    private string FilePath => Path.Combine(SettingsDirectory, "handheld-home.json");

    private static HandheldHomeLayout Arranged() =>
        HandheldHomeLayout.FromSlots(
            [
                HandheldSlot.Recent,
                HandheldSlot.PlatformFolder("ps2"),
                HandheldSlot.Empty,
                HandheldSlot.Game(42),
                HandheldSlot.UserFolder("u-rpg"),
                HandheldSlot.PlatformFolder("gba", isNew: true),
            ],
            [new HandheldUserFolder("u-rpg", "RPGs", "#9B7BEA", [42, 7, 13], HandheldFolderSort.LastPlayed)])
        .WithSlot(23, HandheldSlot.PlatformFolder("gc"));

    private static void AssertSame(HandheldHomeLayout expected, HandheldHomeLayout? actual)
    {
        Assert.NotNull(actual);
        Assert.Equal(expected.AllSlots(), actual.AllSlots());
        var folder = Assert.Single(actual.Folders);
        Assert.Equal("u-rpg", folder.Id);
        Assert.Equal("RPGs", folder.Name);
        Assert.Equal("#9B7BEA", folder.Color);
        Assert.Equal([42, 7, 13], folder.GameIds);
        Assert.Equal(HandheldFolderSort.LastPlayed, folder.Sort);
    }

    [Fact]
    public void MissingFile_ReadsAsNoLayoutYet()
    {
        Assert.Null(new FileHandheldHomeLayoutStore(FilePath).Load());
    }

    [Fact]
    public void SaveThenLoad_RoundTripsEverySlotKind_TheRibbon_AndYourFolders()
    {
        var layout = Arranged();

        new FileHandheldHomeLayoutStore(FilePath).Save(layout);
        var loaded = new FileHandheldHomeLayoutStore(FilePath).Load();

        AssertSame(layout, loaded);
        Assert.Equal(2, loaded!.Pages.Count);
        Assert.True(loaded.SlotAt(5).IsNew);
    }

    [Fact]
    public void SaveThenLoad_KeepsTheSettingsTile_OnHomeOrInYourFolder()
    {
        // M45 H6a, decision 18: on home where you moved it…
        var onHome = HandheldHomeLayout.FromSlots([HandheldSlot.PlatformFolder("ps2"), HandheldSlot.Empty, HandheldSlot.Settings]);
        new FileHandheldHomeLayoutStore(FilePath).Save(onHome);
        Assert.Equal(2, new FileHandheldHomeLayoutStore(FilePath).Load()!.IndexOf(HandheldSlot.Settings));
        Assert.Contains("\"tile\": \"settings\"", File.ReadAllText(FilePath), StringComparison.Ordinal);

        // …or folded into one of your folders.
        var folded = onHome.WithSlot(3, HandheldSlot.UserFolder("u-a"))
            .WithFolder(new HandheldUserFolder("u-a", "A", "#3BBD79", [5]))
            .WithSettingsIn("u-a");
        new FileHandheldHomeLayoutStore(FilePath).Save(folded);
        var loaded = new FileHandheldHomeLayoutStore(FilePath).Load()!;
        Assert.Equal(-1, loaded.IndexOf(HandheldSlot.Settings));
        Assert.True(loaded.FindFolder("u-a")!.HoldsSettings);
    }

    [Fact]
    public void ReinstallStyleRestore_ACopiedSettingsFolder_GivesBackTheSameHome()
    {
        new FileHandheldHomeLayoutStore(FilePath).Save(Arranged());

        // A reinstall: the app's data is gone, then the user's Settings/ folder is copied back beside it.
        var restored = Path.Combine(_directory, "Restored", "Settings");
        Directory.CreateDirectory(restored);
        foreach (var file in Directory.GetFiles(SettingsDirectory))
            File.Copy(file, Path.Combine(restored, Path.GetFileName(file)));
        Directory.Delete(SettingsDirectory, recursive: true);

        AssertSame(Arranged(), new FileHandheldHomeLayoutStore(Path.Combine(restored, "handheld-home.json")).Load());
    }

    [Fact]
    public void ADamagedFile_ReadsAsNoLayout_AndSavingReplacesIt()
    {
        Directory.CreateDirectory(SettingsDirectory);
        File.WriteAllText(FilePath, "{ \"slots\": [ { \"kind\": ");
        var store = new FileHandheldHomeLayoutStore(FilePath);

        Assert.Null(store.Load());

        store.Save(Arranged());
        AssertSame(Arranged(), store.Load());
        Assert.False(File.Exists(FilePath + ".tmp"));
    }

    [Fact]
    public void UnknownOrIncompleteEntries_BecomeEmptySlots_AndBadColoursAreReplaced()
    {
        Directory.CreateDirectory(SettingsDirectory);
        File.WriteAllText(FilePath, """
            {
              "version": 1,
              "slots": [
                { "kind": "platformFolder", "system": "ps2" },
                { "kind": "platformFolder" },
                null,
                { "kind": "game", "game": 0 },
                { "kind": "userFolder", "folder": "u-a" }
              ],
              "folders": [ { "id": "u-a", "name": "", "color": "red", "games": [1, 2] } ]
            }
            """);

        var layout = new FileHandheldHomeLayoutStore(FilePath).Load()!;

        Assert.Equal(HandheldSlot.PlatformFolder("ps2"), layout.SlotAt(0));
        Assert.True(layout.SlotAt(1).IsEmpty);
        Assert.True(layout.SlotAt(3).IsEmpty);
        var folder = Assert.Single(layout.Folders);
        Assert.Equal("Folder", folder.Name);
        Assert.Equal(HandheldHomeLayout.FolderColors[0], folder.Color);
    }

    [Fact]
    public void TheFileIsReadableJson_WithoutTrailingEmptySlots()
    {
        new FileHandheldHomeLayoutStore(FilePath).Save(HandheldHomeLayout.FromSlots([HandheldSlot.Recent, HandheldSlot.PlatformFolder("ps2")]));

        var text = File.ReadAllText(FilePath);
        Assert.Contains("\"platformFolder\"", text);
        Assert.Contains("\"ps2\"", text);
        using var json = System.Text.Json.JsonDocument.Parse(text);
        Assert.Equal(2, json.RootElement.GetProperty("slots").GetArrayLength());
    }

    public void Dispose()
    {
        if (Directory.Exists(_directory))
            Directory.Delete(_directory, recursive: true);
    }
}
