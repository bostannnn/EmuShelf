using EmuShelf.App.Services;
using EmuShelf.App.ViewModels;
using EmuShelf.Core.Launching;
using EmuShelf.Core.Metadata;
using EmuShelf.Core.TexturePacks;
using EmuShelf.Integrations.Emulators;
using EmuShelf.Integrations.Emulators.DuckStation;
using EmuShelf.Integrations.Systems;

namespace EmuShelf.App.Tests;

/// <summary>
/// The Settings texture section over a faked context: totals, filters, and the read-only surface.
/// </summary>
public sealed class TexturePackSettingsSectionTests
{
    private readonly FakeDialogService _dialogs = new();
    private readonly StubConfigurationStore _configurations = new();

    [Fact]
    public void WithoutATextureContext_TheSectionIsNotOffered()
    {
        var viewModel = CreateViewModel();

        Assert.False(viewModel.HasTexturePacks);
        Assert.DoesNotContain(SettingsSection.TexturePacks, viewModel.Sections);
    }

    [Fact]
    public void WithATextureContext_TheSectionAppearsAfterTheOthers()
    {
        var viewModel = CreateViewModel(Context());

        Assert.True(viewModel.HasTexturePacks);
        Assert.Equal(SettingsSection.TexturePacks, viewModel.Sections[^1]);
    }

    [Fact]
    public void TheSummary_CountsMatchedNoMatchAndAttentionSeparately()
    {
        var viewModel = CreateViewModel(Context(
            Usable("SLUS-00594"),
            Usable("SLUS-11111"),
            DumpsOnly("SLUS-22222")));

        Assert.Contains("1 matched", viewModel.TexturePackSummary, StringComparison.Ordinal);
        Assert.Contains("1 with no game in your library", viewModel.TexturePackSummary, StringComparison.Ordinal);
        Assert.Contains("1 needing attention", viewModel.TexturePackSummary, StringComparison.Ordinal);
    }

    [Fact]
    public void BeforeAScan_TheSummarySaysSoRatherThanReportingZeroPacks()
    {
        var viewModel = CreateViewModel(Context(hasScanned: false));

        Assert.Equal("Not scanned yet.", viewModel.TexturePackSummary);
    }

    [Fact]
    public void TheStatusFilter_SeparatesMatchedFromNoMatchAndAttention()
    {
        var viewModel = CreateViewModel(Context(
            Usable("SLUS-00594"),
            Usable("SLUS-11111"),
            DumpsOnly("SLUS-22222")));

        Assert.Equal(3, viewModel.TexturePackEntries.Count);

        viewModel.TextureStatusFilter = "Matched";
        Assert.Equal("SLUS-00594", Assert.Single(viewModel.TexturePackEntries).PackKey);

        viewModel.TextureStatusFilter = "No game in your library";
        Assert.Equal("SLUS-11111", Assert.Single(viewModel.TexturePackEntries).PackKey);

        // "Needs attention" must not sweep in packs that simply have no imported game.
        viewModel.TextureStatusFilter = "Needs attention";
        Assert.Equal("SLUS-22222", Assert.Single(viewModel.TexturePackEntries).PackKey);
    }

    [Fact]
    public void TheEmulatorFilter_OffersOnlyTheEmulatorsThatActuallyContributedPacks()
    {
        var viewModel = CreateViewModel(Context(Usable("SLUS-00594")));

        Assert.Equal(["All", "DuckStation"], viewModel.TextureEmulatorFilters);

        viewModel.TextureEmulatorFilter = "DuckStation";
        Assert.Single(viewModel.TexturePackEntries);
    }

    [Fact]
    public void AMatchedEntry_NamesTheGameItMatchedByItsLibraryTitle()
    {
        var viewModel = CreateViewModel(Context(Usable("SLUS-00594")));

        var entry = viewModel.TexturePackEntries.Single(e => e.PackKey == "SLUS-00594");
        Assert.Equal(TexturePackEntryStatus.Matched, entry.Status);
        Assert.Equal("Final Fantasy VII", entry.MatchedGames);
        Assert.True(entry.HasMatchedGames);
    }

    [Fact]
    public void AnUnmatchedEntry_IsWordedAsAMissingGameRatherThanABrokenPack()
    {
        var viewModel = CreateViewModel(Context(Usable("SLUS-11111")));

        var entry = Assert.Single(viewModel.TexturePackEntries);
        Assert.Equal("No game in your library", entry.StatusText);
        Assert.Contains("isn't broken", entry.StatusTooltip, StringComparison.Ordinal);
        Assert.False(entry.HasMatchedGames);
    }

    [Fact]
    public async Task Rescan_RefreshesTheTotalsFromTheNewPass()
    {
        var inventory = Result(hasScanned: true);
        var rescans = 0;
        var context = new TexturePackSettingsContext(
            () => inventory,
            () => true,
            (_) =>
            {
                rescans++;
                inventory = Result(hasScanned: true, Usable("SLUS-00594"));
                return Task.FromResult(inventory);
            },
            (_, _) => { },
            new Dictionary<string, string>(StringComparer.Ordinal),
            () => new Dictionary<long, string> { [7] = "Final Fantasy VII" });
        var viewModel = CreateViewModel(context);

        Assert.Empty(viewModel.TexturePackEntries);
        Assert.True(viewModel.HasNoTexturePacks);

        await viewModel.RescanTexturePacksCommand.ExecuteAsync(null);

        Assert.Equal(1, rescans);
        Assert.Single(viewModel.TexturePackEntries);
        Assert.False(viewModel.HasNoTexturePacks);
        Assert.Contains("1 matched", viewModel.TexturePackSummary, StringComparison.Ordinal);
    }

    [Fact]
    public void ThePlatformRows_ExposeNoInstallRepairMoveOrDeleteOperation()
    {
        // The read-only guarantee is a property of the surface, not just of the current wiring.
        var commands = typeof(EmulatorSettingsViewModel)
            .GetProperties()
            .Select(property => property.Name)
            .Where(name => name.Contains("Texture", StringComparison.Ordinal) &&
                name.EndsWith("Command", StringComparison.Ordinal))
            .ToArray();

        Assert.Equal(
            [
                "RescanTexturePacksCommand",
                "OpenTextureFolderCommand",
                "BrowseTextureOverrideCommand",
                "ClearTextureOverrideCommand",
            ],
            commands);
    }

    private static TexturePackSettingsContext Context(
        params TexturePackInventoryEntry[] entries) =>
        Context(hasScanned: true, entries);

    private static TexturePackSettingsContext Context(
        bool hasScanned,
        params TexturePackInventoryEntry[] entries)
    {
        var result = Result(hasScanned, entries);
        return new TexturePackSettingsContext(
            () => result,
            () => hasScanned,
            _ => Task.FromResult(result),
            (_, _) => { },
            new Dictionary<string, string>(StringComparer.Ordinal),
            () => new Dictionary<long, string> { [7] = "Final Fantasy VII" });
    }

    private static TexturePackInventoryResult Result(
        bool hasScanned,
        params TexturePackInventoryEntry[] entries)
    {
        if (!hasScanned)
            return TexturePackInventoryResult.Empty;

        var snapshot = new TexturePackInventorySnapshot(
            DuckStationDefinition.Instance.Id,
            "duckstation:/textures",
            "/textures",
            DateTimeOffset.UtcNow,
            TexturePackRootStatus.Ready,
            entries);
        var map = TexturePackLibraryMap.Build(
            [snapshot],
            new Dictionary<long, IReadOnlyList<GameIdentifier>>
            {
                [7] = [new GameIdentifier(GameIdentifierKind.Serial, "SLUS-00594", "test")],
            });
        return new TexturePackInventoryResult(map, []);
    }

    private static TexturePackInventoryEntry Usable(string packKey) =>
        new(
            packKey,
            $"/textures/{packKey}",
            TexturePackContentStatus.Usable,
            [new TexturePackMatchKey(TexturePackMatchRule.ExactSerial, packKey)]);

    private static TexturePackInventoryEntry DumpsOnly(string packKey) =>
        new(
            packKey,
            $"/textures/{packKey}",
            TexturePackContentStatus.EmptyOrDumpsOnly,
            [new TexturePackMatchKey(TexturePackMatchRule.ExactSerial, packKey)]);

    private EmulatorSettingsViewModel CreateViewModel(TexturePackSettingsContext? texturePacks = null) =>
        new(
            KnownSystems.All,
            KnownEmulators.All,
            KnownSystems.All.ToDictionary(
                system => system.Id,
                _ => (EmulatorConfiguration?)null,
                StringComparer.Ordinal),
            _configurations,
            _dialogs,
            texturePacks: texturePacks);

    private sealed class StubConfigurationStore : IEmulatorConfigurationStore
    {
        private readonly Dictionary<string, EmulatorConfiguration> _saved = new(StringComparer.Ordinal);

        public EmulatorConfiguration? Get(string systemId) => _saved.GetValueOrDefault(systemId);

        public void Save(EmulatorConfiguration configuration) =>
            _saved[configuration.SystemId] = configuration;

        public void SaveAll(IReadOnlyList<EmulatorConfiguration> configurations)
        {
            foreach (var configuration in configurations)
                Save(configuration);
        }
    }
}
