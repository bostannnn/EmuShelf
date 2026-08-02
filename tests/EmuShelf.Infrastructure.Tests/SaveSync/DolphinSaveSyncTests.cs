using System.Buffers.Binary;
using EmuShelf.Core.SaveSync;
using EmuShelf.Infrastructure.SaveSync;
using EmuShelf.Integrations.Emulators.Dolphin;

namespace EmuShelf.Infrastructure.Tests.SaveSync;

public sealed class DolphinSaveSyncTests : TempAppDirectoryTestBase
{
    [Fact]
    public async Task LaunchUserArgument_SelectsTheEffectiveUserDirectory()
    {
        var installation = Path.Combine(BaseDirectory, "Dolphin");
        var user = Path.Combine(BaseDirectory, "relocated user");
        Directory.CreateDirectory(installation);
        WriteDolphinIni(user, "[Core]\nSlotA = 255\nSlotB = 255\n");

        var provider = new DolphinSaveLocationProvider(
            "gamecube",
            installation,
            launchArguments: $"--batch -u \"{user}\" --exec game.iso",
            isWindows: true);

        Assert.Equal(Path.GetFullPath(user), await provider.GetUserDirectoryAsync());
    }

    [Fact]
    public async Task UserDirectoryResolution_CoversPortableFlatpakWindowsAndMacOs()
    {
        var installation = Path.Combine(BaseDirectory, "Dolphin");
        var home = Path.Combine(BaseDirectory, "home");
        var documents = Path.Combine(BaseDirectory, "Documents");
        Directory.CreateDirectory(installation);
        File.WriteAllText(Path.Combine(installation, "portable.txt"), string.Empty);

        Assert.Equal(
            Path.Combine(installation, "User"),
            await CreateProvider(installation, home, documents, isWindows: true).GetUserDirectoryAsync());

        File.Delete(Path.Combine(installation, "portable.txt"));
        Assert.Equal(
            Path.Combine(home, ".var", "app", "org.DolphinEmu.dolphin-emu", "data", "dolphin-emu"),
            await CreateProvider(installation, home, documents, isFlatpak: true).GetUserDirectoryAsync());
        Assert.Equal(
            Path.Combine(documents, "Dolphin Emulator"),
            await CreateProvider(installation, home, documents, isWindows: true).GetUserDirectoryAsync());
        Assert.Equal(
            Path.Combine(home, "Library", "Application Support", "Dolphin"),
            await CreateProvider(installation, home, documents, isMacOS: true).GetUserDirectoryAsync());
    }

    [Fact]
    public async Task RawCards_FollowConfiguredPathsAndRegionSubstitution()
    {
        var user = Path.Combine(BaseDirectory, "user");
        var cards = Path.Combine(BaseDirectory, "custom cards");
        var usa = Path.Combine(cards, "Shared.USA.251.raw");
        var eur = Path.Combine(cards, "Shared.EUR.251.raw");
        Directory.CreateDirectory(cards);
        await File.WriteAllTextAsync(usa, "usa-card");
        await File.WriteAllTextAsync(eur, "eur-card");
        WriteDolphinIni(
            user,
            $"[Core]\nSlotA = 1\nSlotB = 255\nMemcardAPath = {Path.Combine(cards, "Shared.USA.raw")}\n");

        var provider = CreateOverriddenProvider("gamecube", user);
        var units = await provider.GetSaveUnitsAsync();

        Assert.Equal(
            ["dolphin/gc/raw/a/EUR/251", "dolphin/gc/raw/a/USA/251"],
            units.Select(unit => unit.UnitId));
        Assert.Equal(eur, provider.ResolveUnit("dolphin/gc/raw/a/EUR/251")!.Path);
    }

    [Fact]
    public async Task RawCards_KeepDefaultAndSizedVariantsIndependent()
    {
        var user = Path.Combine(BaseDirectory, "user");
        var cards = Path.Combine(BaseDirectory, "cards");
        Directory.CreateDirectory(cards);
        await File.WriteAllTextAsync(Path.Combine(cards, "Shared.USA.raw"), "default");
        await File.WriteAllTextAsync(Path.Combine(cards, "Shared.USA.251.raw"), "small");
        WriteDolphinIni(
            user,
            $"[Core]\nSlotA = 1\nSlotB = 255\nMemcardAPath = {Path.Combine(cards, "Shared.USA.raw")}\n");

        var provider = CreateOverriddenProvider("gamecube", user);
        var units = await provider.GetSaveUnitsAsync();

        Assert.Equal(
            ["dolphin/gc/raw/a/USA", "dolphin/gc/raw/a/USA/251"],
            units.Select(unit => unit.UnitId));
        Assert.Equal(
            Path.Combine(cards, "Shared.EUR.251.raw"),
            provider.ResolveUnit("dolphin/gc/raw/a/EUR/251")!.Path);
    }

    [Fact]
    public async Task GciCards_FollowConfiguredFolderAndExposeSiblingFilesIndependently()
    {
        var user = Path.Combine(BaseDirectory, "user");
        var cardRoot = Path.Combine(BaseDirectory, "cards");
        var usa = Path.Combine(cardRoot, "USA");
        Directory.CreateDirectory(usa);
        var first = Path.Combine(usa, "save-one.gci");
        var second = Path.Combine(usa, "save-two.gci");
        WriteGci(first, "GM8E01", 0x11);
        WriteGci(second, "GM8E01", 0x22);
        WriteDolphinIni(
            user,
            $"[Core]\nSlotA = 8\nSlotB = 255\nGCIFolderAPath = {usa}\n");

        var provider = CreateOverriddenProvider("gamecube", user);
        var units = await provider.GetSaveUnitsAsync();

        Assert.Equal(2, units.Count);
        Assert.All(units, unit => Assert.Equal(SaveUnitKind.File, unit.Kind));
        Assert.Contains(units, unit => unit.UnitId == "dolphin/gc/gci/a/GM8E01");
        Assert.Contains(
            units,
            unit => unit.UnitId.StartsWith("dolphin/gc/gci/a/GM8E01/", StringComparison.Ordinal));
        Assert.Equal(
            [first, second],
            units.Select(unit => provider.ResolveUnit(unit.UnitId)!.Path).Order(StringComparer.Ordinal));
    }

    [Fact]
    public async Task PerGameGciOverride_UsesTheExactConfiguredFolder()
    {
        var user = Path.Combine(BaseDirectory, "user");
        var folder = Path.Combine(BaseDirectory, "one game card");
        Directory.CreateDirectory(folder);
        var save = Path.Combine(folder, "override.gci");
        WriteGci(save, "GZLE01", 0x33);
        WriteDolphinIni(user, "[Core]\nSlotA = 8\nSlotB = 255\n");
        WriteGameSettings(user, "GZLE01", $"[Core]\nGCIFolderAPathOverride = {folder}\n");

        var provider = CreateOverriddenProvider("gamecube", user);
        var unit = Assert.Single(await provider.GetSaveUnitsAsync());

        Assert.Equal("dolphin/gc/gci/a/GZLE01", unit.UnitId);
        Assert.Equal(save, provider.ResolveUnit(unit.UnitId)!.Path);
    }

    [Fact]
    public async Task PerGameSlotOrRawCardOverrides_FailClosedInsteadOfGuessing()
    {
        var user = Path.Combine(BaseDirectory, "user");
        WriteDolphinIni(user, "[Core]\nSlotA = 8\nSlotB = 255\n");
        WriteGameSettings(user, "GM8E01", "[Core]\nSlotA = 1\nMemcardAPath = another.raw\n");

        await Assert.ThrowsAsync<DolphinConfigurationFormatException>(
            () => CreateOverriddenProvider("gamecube", user).GetSaveUnitsAsync());
    }

    [Fact]
    public async Task PerGameRawCardSize_IsSupportedByVariantUnitIds()
    {
        var user = Path.Combine(BaseDirectory, "user");
        var card = Path.Combine(user, "GC", "MemoryCardA.USA.251.raw");
        Directory.CreateDirectory(Path.GetDirectoryName(card)!);
        await File.WriteAllTextAsync(card, "small card");
        WriteDolphinIni(user, "[Core]\nSlotA = 1\nSlotB = 255\n");
        WriteGameSettings(user, "GM8E01", "[Core]\nMemoryCardSize = 4\n");

        var unit = Assert.Single(await CreateOverriddenProvider("gamecube", user).GetSaveUnitsAsync());

        Assert.Equal("dolphin/gc/raw/a/USA/251", unit.UnitId);
    }

    [Fact]
    public async Task SlotsUsingTheSameRawCard_FailClosed()
    {
        var user = Path.Combine(BaseDirectory, "user");
        var card = Path.Combine(BaseDirectory, "cards", "Shared.USA.raw");
        WriteDolphinIni(
            user,
            $"[Core]\nSlotA = 1\nSlotB = 1\nMemcardAPath = {card}\nMemcardBPath = {card}\n");

        var error = await Assert.ThrowsAsync<DolphinConfigurationFormatException>(
            () => CreateOverriddenProvider("gamecube", user).GetSaveUnitsAsync());

        Assert.Contains("same save location", error.Message);
    }

    [Fact]
    public async Task SlotsUsingTheSameGciFolder_FailClosedEvenWhenItIsEmpty()
    {
        var user = Path.Combine(BaseDirectory, "user");
        var folder = Path.Combine(BaseDirectory, "cards", "USA");
        WriteDolphinIni(
            user,
            $"[Core]\nSlotA = 8\nSlotB = 8\nGCIFolderAPath = {folder}\nGCIFolderBPath = {folder}\n");

        await Assert.ThrowsAsync<DolphinConfigurationFormatException>(
            () => CreateOverriddenProvider("gamecube", user).GetSaveUnitsAsync());
    }

    [Fact]
    public async Task NestedGciLayout_FailsClosedInsteadOfSilentlyOmittingSaves()
    {
        var user = Path.Combine(BaseDirectory, "user");
        var usa = Path.Combine(user, "GC", "USA", "Card A");
        Directory.CreateDirectory(Path.Combine(usa, "nested"));
        WriteGci(Path.Combine(usa, "nested", "save.gci"), "GM8E01", 0x44);
        WriteDolphinIni(user, "[Core]\nSlotA = 8\nSlotB = 255\n");

        await Assert.ThrowsAsync<DolphinConfigurationFormatException>(
            () => CreateOverriddenProvider("gamecube", user).GetSaveUnitsAsync());
    }

    [Fact]
    public async Task WiiSaves_FollowConfiguredNandAndExcludeOtherNandData()
    {
        var user = Path.Combine(BaseDirectory, "user");
        var nand = Path.Combine(BaseDirectory, "relocated NAND");
        var save = Path.Combine(nand, "title", "00010000", "52534d45", "data");
        Directory.CreateDirectory(save);
        await File.WriteAllTextAsync(Path.Combine(save, "banner.bin"), "save");
        Directory.CreateDirectory(Path.Combine(nand, "title", "00010001", "48414141", "data"));
        Directory.CreateDirectory(Path.Combine(user, "StateSaves"));
        WriteDolphinIni(user, $"[General]\nNANDRootPath = {nand}\n");

        var provider = CreateOverriddenProvider("wii", user);
        var unit = Assert.Single(await provider.GetSaveUnitsAsync());

        Assert.Equal(new SaveUnit(
            "dolphin/wii/title/00010000/52534d45",
            "52534d45",
            SaveUnitKind.Folder), unit);
        Assert.Equal(save, provider.ResolveUnit(unit.UnitId)!.Path);
    }

    [Fact]
    public async Task WiiSaves_IgnoreEmptyTitleDataDirectories()
    {
        var user = Path.Combine(BaseDirectory, "user");
        var nand = Path.Combine(BaseDirectory, "relocated NAND");
        Directory.CreateDirectory(Path.Combine(nand, "title", "00010000", "52454445", "data"));
        var populated = Path.Combine(nand, "title", "00010000", "52534d45", "data");
        Directory.CreateDirectory(populated);
        await File.WriteAllTextAsync(Path.Combine(populated, "banner.bin"), "save");
        WriteDolphinIni(user, $"[General]\nNANDRootPath = {nand}\n");

        var unit = Assert.Single(await CreateOverriddenProvider("wii", user).GetSaveUnitsAsync());

        Assert.Equal("dolphin/wii/title/00010000/52534d45", unit.UnitId);
    }

    [Fact]
    public async Task DetectionInfo_ReportsExternalSaveLocationsAndValidatesConfiguration()
    {
        var user = Path.Combine(BaseDirectory, "user");
        var usa = Path.Combine(BaseDirectory, "external cards", "USA");
        Directory.CreateDirectory(usa);
        WriteGci(Path.Combine(usa, "save.gci"), "GM8E01", 0x61);
        WriteDolphinIni(
            user,
            $"[Core]\nSlotA = 8\nSlotB = 255\nGCIFolderAPath = {usa}\n");

        var info = await CreateOverriddenProvider("gamecube", user).GetSaveLocationInfoAsync();

        Assert.Equal(user, info.UserDirectory);
        Assert.Equal([usa], info.SaveLocations);
    }

    [Fact]
    public void RemoteOnlyUnits_ResolveToConfiguredDestinations()
    {
        var user = Path.Combine(BaseDirectory, "user");
        var usa = Path.Combine(BaseDirectory, "cards", "USA");
        WriteDolphinIni(
            user,
            $"[Core]\nSlotA = 8\nSlotB = 255\nGCIFolderAPath = {usa}\n");

        var location = CreateOverriddenProvider("gamecube", user)
            .ResolveUnit("dolphin/gc/gci/a/GM8E01");

        Assert.NotNull(location);
        Assert.Equal(Path.Combine(usa, "GM8E01.gci"), location.Path);
        Assert.Equal(usa, location.RootPath);
        Assert.Equal(SaveUnitKind.File, location.Kind);
    }

    [Fact]
    public async Task GciFile_RoundTripsToAnotherConfiguredCardFolder()
    {
        var sourceUser = Path.Combine(BaseDirectory, "source-user");
        var sourceFolder = Path.Combine(BaseDirectory, "source-card", "USA");
        var sourcePath = Path.Combine(sourceFolder, "metroid.gci");
        WriteGci(sourcePath, "GM8E01", 0x55);
        WriteDolphinIni(
            sourceUser,
            $"[Core]\nSlotA = 8\nSlotB = 255\nGCIFolderAPath = {sourceFolder}\n");
        var sourceEndpoint = new FileSystemLocalSaveEndpoint(
            CreateOverriddenProvider("gamecube", sourceUser), AppPaths);
        const string unitId = "dolphin/gc/gci/a/GM8E01";
        var snapshot = await sourceEndpoint.SnapshotAsync(unitId);
        await using var payload = await sourceEndpoint.ReadAsync(unitId);

        var targetUser = Path.Combine(BaseDirectory, "target-user");
        var targetFolder = Path.Combine(BaseDirectory, "target-card", "USA");
        WriteDolphinIni(
            targetUser,
            $"[Core]\nSlotA = 8\nSlotB = 255\nGCIFolderAPath = {targetFolder}\n");
        var targetProvider = CreateOverriddenProvider("gamecube", targetUser);
        var targetEndpoint = new FileSystemLocalSaveEndpoint(targetProvider, AppPaths);

        await targetEndpoint.WriteAsync(
            unitId,
            payload,
            snapshot!.ContentHash,
            snapshot.ModifiedUtc);

        var targetPath = targetProvider.ResolveUnit(unitId)!.Path;
        Assert.Equal(Path.Combine(targetFolder, "GM8E01.gci"), targetPath);
        Assert.Equal(await File.ReadAllBytesAsync(sourcePath), await File.ReadAllBytesAsync(targetPath));
        Assert.Equal(snapshot.ContentHash, (await targetEndpoint.SnapshotAsync(unitId))!.ContentHash);
    }

    [Fact]
    public async Task GciFiles_KeepBaseUnitWhenOneSaveBecomesSeveralAndRoundTripWithoutDuplicates()
    {
        var sourceUser = Path.Combine(BaseDirectory, "source-user");
        var sourceFolder = Path.Combine(BaseDirectory, "source-card", "USA");
        var firstPath = Path.Combine(sourceFolder, "first.gci");
        WriteGci(firstPath, "GM8E01", 0x22);
        WriteDolphinIni(
            sourceUser,
            $"[Core]\nSlotA = 8\nSlotB = 255\nGCIFolderAPath = {sourceFolder}\n");
        var sourceProvider = CreateOverriddenProvider("gamecube", sourceUser);

        var originalUnit = Assert.Single(await sourceProvider.GetSaveUnitsAsync());
        Assert.Equal("dolphin/gc/gci/a/GM8E01", originalUnit.UnitId);

        // 0x11 produces an internal-name identity that sorts before the existing 0x22 file.
        // This deliberately makes the new file take over the stable base unit.
        var secondPath = Path.Combine(sourceFolder, "second.gci");
        WriteGci(secondPath, "GM8E01", 0x11);
        var expandedUnits = await sourceProvider.GetSaveUnitsAsync();
        Assert.Equal(2, expandedUnits.Count);
        Assert.Contains(expandedUnits, unit => unit.UnitId == originalUnit.UnitId);

        var sourceEndpoint = new FileSystemLocalSaveEndpoint(sourceProvider, AppPaths);
        var targetUser = Path.Combine(BaseDirectory, "target-user");
        var targetFolder = Path.Combine(BaseDirectory, "target-card", "USA");
        WriteDolphinIni(
            targetUser,
            $"[Core]\nSlotA = 8\nSlotB = 255\nGCIFolderAPath = {targetFolder}\n");
        var originalTargetPath = Path.Combine(targetFolder, "existing.gci");
        WriteGci(originalTargetPath, "GM8E01", 0x22);
        var targetProvider = CreateOverriddenProvider("gamecube", targetUser);
        var targetEndpoint = new FileSystemLocalSaveEndpoint(targetProvider, AppPaths);
        var siblingUnit = Assert.Single(expandedUnits, unit => unit.UnitId != originalUnit.UnitId);

        Assert.Equal(originalTargetPath, targetProvider.ResolveUnit(originalUnit.UnitId)!.Path);
        Assert.NotEqual(originalTargetPath, targetProvider.ResolveUnit(siblingUnit.UnitId)!.Path);

        foreach (var unit in expandedUnits.OrderBy(unit => unit.UnitId, StringComparer.Ordinal))
        {
            var snapshot = await sourceEndpoint.SnapshotAsync(unit.UnitId);
            await using var payload = await sourceEndpoint.ReadAsync(unit.UnitId);
            await targetEndpoint.WriteAsync(
                unit.UnitId,
                payload,
                snapshot!.ContentHash,
                snapshot.ModifiedUtc);
        }

        Assert.Equal(
            expandedUnits.Select(unit => unit.UnitId),
            (await targetProvider.GetSaveUnitsAsync()).Select(unit => unit.UnitId));

        // Repeat the transition from two files to three. The new 0xF2 identity sorts before both
        // existing files, so the former base must become a distinct sibling rather than aliasing
        // the path that the new base will overwrite.
        WriteGci(Path.Combine(sourceFolder, "third.gci"), "GM8E01", 0xF2);
        var threeUnits = await sourceProvider.GetSaveUnitsAsync();
        var formerBase = Assert.Single(
            threeUnits,
            unit => sourceProvider.ResolveUnit(unit.UnitId)!.Path == secondPath);
        var currentTargetBasePath = targetProvider.ResolveUnit(originalUnit.UnitId)!.Path;
        Assert.NotEqual(currentTargetBasePath, targetProvider.ResolveUnit(formerBase.UnitId)!.Path);

        foreach (var unit in threeUnits.OrderBy(unit => unit.UnitId, StringComparer.Ordinal))
        {
            var snapshot = await sourceEndpoint.SnapshotAsync(unit.UnitId);
            await using var payload = await sourceEndpoint.ReadAsync(unit.UnitId);
            await targetEndpoint.WriteAsync(
                unit.UnitId,
                payload,
                snapshot!.ContentHash,
                snapshot.ModifiedUtc);
        }

        Assert.Equal(
            threeUnits.Select(unit => unit.UnitId),
            (await targetProvider.GetSaveUnitsAsync()).Select(unit => unit.UnitId));
    }

    [Fact]
    public async Task RealDolphinInstallation_ResolvesConfiguredUserAndSaveLocations()
    {
        // Opt-in: set EMUSHELF_TEST_DOLPHIN_DIR to verify a real installation and its read-only
        // Dolphin.ini/GameSettings path resolution without assuming that its saves use defaults.
        var installation = Environment.GetEnvironmentVariable("EMUSHELF_TEST_DOLPHIN_DIR");
        if (string.IsNullOrWhiteSpace(installation))
            return;

        foreach (var systemId in new[] { "gamecube", "wii" })
        {
            var provider = new DolphinSaveLocationProvider(systemId, installation, isWindows: true);
            Assert.True(Directory.Exists(await provider.GetUserDirectoryAsync()));
            foreach (var unit in await provider.GetSaveUnitsAsync())
                Assert.NotNull(provider.ResolveUnit(unit.UnitId));
        }
    }

    private DolphinSaveLocationProvider CreateOverriddenProvider(string systemId, string user) =>
        new(systemId, Path.Combine(BaseDirectory, "Dolphin"), userDirectoryOverride: user);

    private static DolphinSaveLocationProvider CreateProvider(
        string installation,
        string home,
        string documents,
        bool isWindows = false,
        bool isMacOS = false,
        bool isFlatpak = false) =>
        new(
            "gamecube",
            installation,
            isFlatpak: isFlatpak,
            homeDirectory: home,
            documentsDirectory: documents,
            isWindows: isWindows,
            isMacOS: isMacOS);

    private static void WriteDolphinIni(string user, string contents)
    {
        var config = Path.Combine(user, "Config");
        Directory.CreateDirectory(config);
        File.WriteAllText(Path.Combine(config, "Dolphin.ini"), contents);
    }

    private static void WriteGameSettings(string user, string gameId, string contents)
    {
        var settings = Path.Combine(user, "GameSettings");
        Directory.CreateDirectory(settings);
        File.WriteAllText(Path.Combine(settings, gameId + ".ini"), contents);
    }

    private static void WriteGci(string path, string gameId, byte content)
    {
        const int headerSize = 0x40;
        const int blockSize = 0x2000;
        var bytes = Enumerable.Repeat(content, headerSize + blockSize).ToArray();
        for (var index = 0; index < gameId.Length; index++)
            bytes[index] = (byte)gameId[index];
        BinaryPrimitives.WriteUInt16BigEndian(bytes.AsSpan(0x38, 2), 1);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllBytes(path, bytes);
    }
}
