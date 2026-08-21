using System.Buffers.Binary;
using EmuShelf.Core.SaveSync;
using EmuShelf.Integrations.Emulators.Dolphin;

namespace EmuShelf.Infrastructure.Tests.SaveSync;

public sealed class DolphinAndroidSaveSyncTests : TempAppDirectoryTestBase
{
    [Fact]
    public async Task GameCube_DefaultCardAFolder_UsesDesktopCompatibleUnitIdAndRestorePath()
    {
        var files = Path.Combine(BaseDirectory, "Android", "data", "org.dolphinemu.dolphinemu", "files");
        var card = Path.Combine(files, "GC", "USA", "Card A");
        var save = Path.Combine(card, "observed-name.gci");
        WriteGci(save, "GM8E01", 0x31);

        var provider = CreateProvider("gamecube", files);
        var unit = Assert.Single(await provider.GetSaveUnitsAsync());

        Assert.Equal("gamecube/gci/a/GM8E01", unit.UnitId);
        Assert.Equal(SaveUnitKind.File, unit.Kind);
        Assert.Equal(save, provider.ResolveUnit(unit.UnitId)!.Path);
        Assert.Equal(
            Path.Combine(card, "GZLE01.gci"),
            provider.ResolveUnit("gamecube/gci/a/GZLE01")!.Path);
    }

    [Fact]
    public async Task GameCube_ConfiguredCardBFolder_PreservesSlotAndRegionMapping()
    {
        var files = Path.Combine(BaseDirectory, "Android", "data", "org.dolphinemu.dolphinemu", "files");
        var config = Path.Combine(files, "Config");
        Directory.CreateDirectory(config);
        File.WriteAllText(
            Path.Combine(config, "Dolphin.ini"),
            "[Core]\nSlotA = 255\nSlotB = 8\n");
        var save = Path.Combine(files, "GC", "EUR", "Card B", "zelda.gci");
        WriteGci(save, "GZLP01", 0x42);

        var provider = CreateProvider("gamecube", files);
        var unit = Assert.Single(await provider.GetSaveUnitsAsync());

        Assert.Equal("gamecube/gci/b/GZLP01", unit.UnitId);
        Assert.Equal(save, provider.ResolveUnit(unit.UnitId)!.Path);
    }

    [Fact]
    public async Task Wii_TitleData_UsesTheSamePerTitleFolderUnitAsDesktop()
    {
        var files = Path.Combine(BaseDirectory, "Android", "data", "org.dolphinemu.dolphinemu", "files");
        var data = Path.Combine(files, "Wii", "title", "00010000", "524d4345", "data");
        Directory.CreateDirectory(data);
        File.WriteAllText(Path.Combine(data, "save.dat"), "save");

        var provider = CreateProvider("wii", files);
        var unit = Assert.Single(await provider.GetSaveUnitsAsync());

        Assert.Equal("wii/title/00010000/524d4345", unit.UnitId);
        Assert.Equal(SaveUnitKind.Folder, unit.Kind);
        Assert.Equal(data, provider.ResolveUnit(unit.UnitId)!.Path);
    }

    private static DolphinSaveLocationProvider CreateProvider(string systemId, string filesDirectory) =>
        new(
            systemId,
            filesDirectory,
            userDirectoryOverride: filesDirectory,
            isWindows: false,
            isMacOS: false);

    private static void WriteGci(string path, string gameId, byte marker)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var bytes = new byte[0x40 + 0x2000];
        System.Text.Encoding.ASCII.GetBytes(gameId).CopyTo(bytes, 0);
        Array.Fill(bytes, marker, 0x08, 0x20);
        BinaryPrimitives.WriteUInt16BigEndian(bytes.AsSpan(0x38, 2), 1);
        File.WriteAllBytes(path, bytes);
    }
}
