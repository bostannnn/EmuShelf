using EmuShelf.Core.SaveSync;
using EmuShelf.Infrastructure.SaveSync;
using EmuShelf.Infrastructure.Storage;
using EmuShelf.Integrations.Emulators.DuckStation;

namespace EmuShelf.Infrastructure.Tests.SaveSync;

public sealed class DuckStationSaveSyncTests : TempAppDirectoryTestBase
{
    [Fact]
    public async Task Windows_UsesExistingLegacyDirectoryBeforeCurrentDirectory()
    {
        var localAppData = Path.Combine(BaseDirectory, "LocalAppData");
        var documents = Path.Combine(BaseDirectory, "Documents");
        var current = Path.Combine(localAppData, "DuckStation");
        var legacy = Path.Combine(documents, "DuckStation");
        WriteSettings(current, "current-cards");
        WriteSettings(legacy, "legacy-cards");

        var provider = CreateWindowsProvider(localAppData: localAppData, documents: documents);

        Assert.Equal(
            Path.Combine(legacy, "legacy-cards"),
            await provider.GetMemoryCardsDirectoryAsync());
    }

    [Fact]
    public async Task Windows_UsesCurrentUserDirectoryWhenLegacyDirectoryIsAbsent()
    {
        var localAppData = Path.Combine(BaseDirectory, "LocalAppData");
        var documents = Path.Combine(BaseDirectory, "Documents");
        var current = Path.Combine(localAppData, "DuckStation");
        WriteSettings(current, "memcards");

        var provider = CreateWindowsProvider(localAppData: localAppData, documents: documents);

        Assert.Equal(Path.Combine(current, "memcards"), await provider.GetMemoryCardsDirectoryAsync());
    }

    [Fact]
    public async Task Windows_ExistingLegacyDirectoryWithoutSettingsFailsClosed()
    {
        var localAppData = Path.Combine(BaseDirectory, "LocalAppData");
        var documents = Path.Combine(BaseDirectory, "Documents");
        Directory.CreateDirectory(Path.Combine(documents, "DuckStation"));
        WriteSettings(Path.Combine(localAppData, "DuckStation"), "inactive-current-cards");
        var provider = CreateWindowsProvider(localAppData: localAppData, documents: documents);

        await Assert.ThrowsAsync<DuckStationConfigurationFormatException>(
            () => provider.GetMemoryCardsDirectoryAsync());
    }

    [Fact]
    public async Task PortableMarker_UsesSettingsBesideExecutable()
    {
        var installation = Path.Combine(BaseDirectory, "DuckStation-portable");
        var localAppData = Path.Combine(BaseDirectory, "LocalAppData");
        Directory.CreateDirectory(installation);
        File.WriteAllText(Path.Combine(installation, "portable.txt"), string.Empty);
        WriteSettings(installation, "portable-cards");
        WriteSettings(Path.Combine(localAppData, "DuckStation"), "global-cards");

        var provider = CreateWindowsProvider(installation, localAppData: localAppData);

        Assert.Equal(
            Path.Combine(installation, "portable-cards"),
            await provider.GetMemoryCardsDirectoryAsync());
    }

    [Fact]
    public async Task SettingsBesideExecutable_AlsoSelectsPortableModeWithoutMarker()
    {
        var installation = Path.Combine(BaseDirectory, "DuckStation-portable");
        var localAppData = Path.Combine(BaseDirectory, "LocalAppData");
        WriteSettings(installation, "portable-cards");
        WriteSettings(Path.Combine(localAppData, "DuckStation"), "global-cards");

        var provider = CreateWindowsProvider(installation, localAppData: localAppData);

        Assert.Equal(
            Path.Combine(installation, "portable-cards"),
            await provider.GetMemoryCardsDirectoryAsync());
    }

    [Fact]
    public async Task Flatpak_UsesCurrentConfigDirectoryAndSupportsLegacyDataDirectory()
    {
        var home = Path.Combine(BaseDirectory, "home");
        var current = Path.Combine(
            home, ".var", "app", "org.duckstation.DuckStation", "config", "duckstation");
        WriteSettings(current, "cards");
        var placeholderInstallation = Path.Combine(BaseDirectory, "unused-install");
        WriteSettings(placeholderInstallation, "unrelated-cards");

        var provider = new DuckStationSaveLocationProvider(
            placeholderInstallation,
            homeDirectory: home,
            isWindows: false,
            isMacOS: false,
            isFlatpak: true);

        Assert.Equal(Path.Combine(current, "cards"), await provider.GetMemoryCardsDirectoryAsync());

        File.Delete(Path.Combine(current, "settings.ini"));
        Directory.Delete(current);
        var legacy = Path.Combine(
            home, ".var", "app", "org.duckstation.DuckStation", "data", "duckstation");
        WriteSettings(legacy, "legacy-cards");

        Assert.Equal(Path.Combine(legacy, "legacy-cards"), await provider.GetMemoryCardsDirectoryAsync());
    }

    [Fact]
    public async Task LinuxAndMacOsUseTheirPlatformUserDataDirectories()
    {
        var home = Path.Combine(BaseDirectory, "home");
        var xdg = Path.Combine(BaseDirectory, "xdg-data");
        var linuxUser = Path.Combine(xdg, "duckstation");
        var macUser = Path.Combine(home, "Library", "Application Support", "DuckStation");
        WriteSettings(linuxUser, "linux-cards");
        WriteSettings(macUser, "mac-cards");
        var linux = new DuckStationSaveLocationProvider(
            Path.Combine(BaseDirectory, "install"),
            homeDirectory: home,
            xdgConfigHome: xdg,
            isWindows: false,
            isMacOS: false);
        var mac = new DuckStationSaveLocationProvider(
            Path.Combine(BaseDirectory, "install"),
            homeDirectory: home,
            isWindows: false,
            isMacOS: true);

        Assert.Equal(Path.Combine(linuxUser, "linux-cards"), await linux.GetMemoryCardsDirectoryAsync());
        Assert.Equal(Path.Combine(macUser, "mac-cards"), await mac.GetMemoryCardsDirectoryAsync());
    }

    [Fact]
    public async Task MissingSettingsNeverFallsBackToAnAssumedCardPath()
    {
        var selectedDirectory = Path.Combine(BaseDirectory, "selected");
        Directory.CreateDirectory(selectedDirectory);
        var provider = new DuckStationSaveLocationProvider(
            Path.Combine(BaseDirectory, "install"),
            userDirectoryOverride: selectedDirectory);

        var exception = await Assert.ThrowsAsync<DuckStationConfigurationFormatException>(
            () => provider.GetMemoryCardsDirectoryAsync());

        Assert.IsAssignableFrom<SaveProviderConfigurationException>(exception);
    }

    [Fact]
    public async Task EnumeratesEnabledSharedAndPerGameCardsWithoutSaveStatesOrInactiveCards()
    {
        var userDirectory = Path.Combine(BaseDirectory, "user");
        var cards = Path.Combine(userDirectory, "cards");
        var shared = Path.Combine(cards, "shared", "family.mcd");
        Directory.CreateDirectory(Path.GetDirectoryName(shared)!);
        File.WriteAllText(shared, "shared");
        Directory.CreateDirectory(cards);
        File.WriteAllText(Path.Combine(cards, "SLUS-01041_2.mcd"), "per-game");
        File.WriteAllText(Path.Combine(cards, "Final Fantasy VII_2.mcd"), "per-title");
        File.WriteAllText(Path.Combine(cards, "shared_card_2.mcd"), "stale shared card");
        File.WriteAllText(Path.Combine(cards, "SLUS-00001_1.mcd"), "slot 1 is not per-game");
        File.WriteAllText(Path.Combine(cards, "state_2.sav"), "save state");
        WriteSettings(
            userDirectory,
            "cards",
            card1Type: "Shared",
            card2Type: "PerGameTitle",
            card1Path: "shared/family.mcd");
        var provider = ProviderFor(userDirectory);

        Assert.Equal(
            [
                new SaveUnit("duckstation/shared/card1", "Shared memory card 1 (used by every game)", SaveUnitKind.File),
                new SaveUnit("duckstation/per-game/title/Final Fantasy VII_2.mcd", "Final Fantasy VII_2.mcd", SaveUnitKind.File),
                new SaveUnit("duckstation/per-game/title/SLUS-01041_2.mcd", "SLUS-01041_2.mcd", SaveUnitKind.File),
            ],
            await provider.GetSaveUnitsAsync());
    }

    [Fact]
    public async Task SharedCardHonorsAbsolutePathAndDefaultFileNameWithinConfiguredDirectory()
    {
        var userDirectory = Path.Combine(BaseDirectory, "user");
        var externalCard = Path.Combine(BaseDirectory, "external", "card-one.mcd");
        Directory.CreateDirectory(Path.GetDirectoryName(externalCard)!);
        File.WriteAllText(externalCard, "one");
        var cards = Path.Combine(userDirectory, "cards");
        Directory.CreateDirectory(cards);
        File.WriteAllText(Path.Combine(cards, "shared_card_2.mcd"), "two");
        WriteSettings(
            userDirectory,
            "cards",
            card1Type: "Shared",
            card2Type: "Shared",
            card1Path: externalCard);
        var provider = ProviderFor(userDirectory);

        var units = await provider.GetSaveUnitsAsync();

        Assert.Equal(2, units.Count);
        Assert.Equal(externalCard, provider.ResolveUnit("duckstation/shared/card1")!.Path);
        Assert.Equal(
            Path.Combine(cards, "shared_card_2.mcd"),
            provider.ResolveUnit("duckstation/shared/card2")!.Path);
    }

    [Fact]
    public void ResolveUnitAllowsRemoteOnlyActivePerGameCardsAndRejectsUnsafeOrInactiveIds()
    {
        var userDirectory = Path.Combine(BaseDirectory, "user");
        WriteSettings(userDirectory, "cards", card1Type: "PerGame", card2Type: "None");
        var provider = ProviderFor(userDirectory);

        var location = provider.ResolveUnit("duckstation/per-game/serial/SCUS-94163_1.mcd");

        Assert.NotNull(location);
        Assert.Equal(
            Path.Combine(userDirectory, "cards", "SCUS-94163_1.mcd"),
            location.Path);
        Assert.Null(provider.ResolveUnit("duckstation/per-game/serial/SCUS-94163_2.mcd"));
        Assert.Null(provider.ResolveUnit("duckstation/per-game/serial/../SCUS-94163_1.mcd"));
        Assert.Null(provider.ResolveUnit("duckstation/per-game/title/SCUS-94163_1.mcd"));
        Assert.Null(provider.ResolveUnit("duckstation/per-game/serial/Final Fantasy VII_1.mcd"));
        Assert.Null(provider.ResolveUnit("duckstation/per-game/SCUS-94163_1.mcd"));
        Assert.Null(provider.ResolveUnit("duckstation/shared/card1"));
        Assert.Null(provider.ResolveUnit("pcsx2/SCUS-94163_1.mcd"));
    }

    [Fact]
    public async Task SerialModeExcludesStaleTitleCardsAndUsesASchemeSpecificIdentity()
    {
        var userDirectory = Path.Combine(BaseDirectory, "user");
        var cards = Path.Combine(userDirectory, "cards");
        Directory.CreateDirectory(cards);
        File.WriteAllText(Path.Combine(cards, "SCUS-94163_1.mcd"), "serial");
        File.WriteAllText(Path.Combine(cards, "Final Fantasy VII_1.mcd"), "stale title");
        WriteSettings(userDirectory, "cards", card1Type: "PerGame", card2Type: "None");
        var provider = ProviderFor(userDirectory);

        Assert.Equal(
            [new SaveUnit(
                "duckstation/per-game/serial/SCUS-94163_1.mcd",
                "SCUS-94163_1.mcd",
                SaveUnitKind.File)],
            await provider.GetSaveUnitsAsync());
    }

    [Fact]
    public async Task FileTitleCardsFailClosedUntilTheyHaveAStableCrossMachineIdentity()
    {
        var userDirectory = Path.Combine(BaseDirectory, "user");
        WriteSettings(userDirectory, "cards", card1Type: "PerGameFileTitle", card2Type: "None");
        var provider = ProviderFor(userDirectory);

        var exception = await Assert.ThrowsAsync<DuckStationConfigurationFormatException>(
            () => provider.GetSaveUnitsAsync());

        Assert.Contains("stable cross-machine identity", exception.Message);
    }

    [Fact]
    public async Task UnknownEnabledCardTypeFailsClosed()
    {
        var userDirectory = Path.Combine(BaseDirectory, "user");
        WriteSettings(userDirectory, "cards", card1Type: "FutureCardType", card2Type: "None");
        var provider = ProviderFor(userDirectory);

        await Assert.ThrowsAsync<DuckStationConfigurationFormatException>(
            () => provider.GetSaveUnitsAsync());
    }

    [Fact]
    public async Task PerGameCardRoundTripsToAnEmptySecondMachine()
    {
        var pathsA = new AppPaths(Path.Combine(BaseDirectory, "machine-a"));
        var pathsB = new AppPaths(Path.Combine(BaseDirectory, "machine-b"));
        pathsA.EnsureDirectoriesExist();
        pathsB.EnsureDirectoriesExist();
        var userA = Path.Combine(pathsA.BaseDirectory, "duckstation");
        var userB = Path.Combine(pathsB.BaseDirectory, "duckstation");
        WriteSettings(userA, "cards", card1Type: "PerGame", card2Type: "None");
        WriteSettings(userB, "cards", card1Type: "PerGame", card2Type: "None");
        var cardA = Path.Combine(userA, "cards", "SCUS-94163_1.mcd");
        Directory.CreateDirectory(Path.GetDirectoryName(cardA)!);
        await File.WriteAllTextAsync(cardA, "progress");
        var providerA = ProviderFor(userA);
        var providerB = ProviderFor(userB);
        var remote = new InMemoryCloudSyncTransport();
        var serviceA = new SaveSyncService(
            new FileSystemLocalSaveEndpoint(providerA, pathsA),
            remote,
            new JsonSaveSyncManifestStore(pathsA));
        var serviceB = new SaveSyncService(
            new FileSystemLocalSaveEndpoint(providerB, pathsB),
            remote,
            new JsonSaveSyncManifestStore(pathsB));

        Assert.Equal(1, (await serviceA.SyncAsync(providerA)).Uploaded);
        Assert.Equal(1, (await serviceB.SyncAsync(providerB)).Downloaded);
        Assert.Equal(
            "progress",
            await File.ReadAllTextAsync(Path.Combine(userB, "cards", "SCUS-94163_1.mcd")));
    }

    private DuckStationSaveLocationProvider CreateWindowsProvider(
        string? installation = null,
        string? localAppData = null,
        string? documents = null) =>
        new(
            installation ?? Path.Combine(BaseDirectory, "install"),
            homeDirectory: Path.Combine(BaseDirectory, "home"),
            localApplicationDataDirectory: localAppData ?? Path.Combine(BaseDirectory, "LocalAppData"),
            documentsDirectory: documents ?? Path.Combine(BaseDirectory, "Documents"),
            isWindows: true,
            isMacOS: false);

    private DuckStationSaveLocationProvider ProviderFor(string userDirectory) =>
        new(Path.Combine(BaseDirectory, "install"), userDirectoryOverride: userDirectory);

    private static void WriteSettings(
        string userDirectory,
        string cardsDirectory,
        string card1Type = "None",
        string card2Type = "None",
        string? card1Path = null,
        string? card2Path = null)
    {
        Directory.CreateDirectory(userDirectory);
        var lines = new List<string>
        {
            "[Main]",
            "SettingsVersion = 3",
            "[MemoryCards]",
            $"Directory = {cardsDirectory}",
            $"Card1Type = {card1Type}",
            $"Card2Type = {card2Type}",
        };
        if (card1Path is not null)
            lines.Add($"Card1Path = {card1Path}");
        if (card2Path is not null)
            lines.Add($"Card2Path = {card2Path}");
        File.WriteAllLines(Path.Combine(userDirectory, "settings.ini"), lines);
    }
}
