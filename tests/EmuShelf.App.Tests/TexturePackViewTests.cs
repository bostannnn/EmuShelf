using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Avalonia.VisualTree;
using EmuShelf.App.Services;
using EmuShelf.App.ViewModels;
using EmuShelf.App.Views;
using EmuShelf.Core.Launching;
using EmuShelf.Core.Metadata;
using EmuShelf.Core.TexturePacks;
using EmuShelf.Integrations.Emulators;
using EmuShelf.Integrations.Emulators.DuckStation;
using EmuShelf.Integrations.Systems;

namespace EmuShelf.App.Tests;

/// <summary>
/// Renders the real windows so the texture marks and the Settings section are proven to bind,
/// rather than only proven to compile.
/// </summary>
public class TexturePackViewTests
{
    [AvaloniaFact]
    public void SettingsWindow_RendersThePackListAndItsRowActionsResolveTheirCommands()
    {
        var viewModel = CreateSettingsViewModel();
        viewModel.SelectedSection = SettingsSection.TexturePacks;
        var window = new EmulatorSettingsWindow { DataContext = viewModel };
        window.Show();
        try
        {
            Dispatcher.UIThread.RunJobs();

            // The per-pack list is collapsed by default to keep large libraries readable; expand it.
            var expander = window.GetVisualDescendants().OfType<Expander>().Single();
            expander.IsExpanded = true;
            Dispatcher.UIThread.RunJobs();

            // The pack list rendered the entry the fake context supplied.
            var packText = window.GetVisualDescendants()
                .OfType<TextBlock>()
                .Select(block => block.Text)
                .ToArray();
            Assert.Contains("SLUS-00594", packText);
            Assert.Contains("Final Fantasy VII", packText);

            // The per-platform buttons use $parent[Window] bindings to reach the view model's
            // commands. A broken path leaves Command null, which compiles but does nothing.
            var buttons = window.GetVisualDescendants().OfType<Button>().ToArray();
            foreach (var content in new[] { "Rescan", "Browse...", "Use detected", "Open folder" })
            {
                var button = buttons.FirstOrDefault(candidate => Equals(candidate.Content, content));
                Assert.NotNull(button);
                Assert.NotNull(button.Command);
            }
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaFact]
    public void SettingsWindow_MultiEmulatorRow_RendersTheEmulatorProfilePicker()
    {
        // The PlayStation row now has two profiles (DuckStation / RetroArch), so its picker must
        // render and bind. Nothing else renders the Emulators section, so this is its binding proof.
        var viewModel = CreateSettingsViewModel();
        viewModel.SelectedSection = SettingsSection.Emulators;
        var window = new EmulatorSettingsWindow { DataContext = viewModel };
        window.Show();
        try
        {
            viewModel.Rows.Single(row => row.SystemId == "playstation").IsExpanded = true;
            Dispatcher.UIThread.RunJobs();

            var profileNames = window.GetVisualDescendants()
                .OfType<ComboBox>()
                .Select(box => box.ItemsSource)
                .OfType<IEnumerable<EmulatorSettingsRowViewModel.EmulatorProfileOption>>()
                .SelectMany(options => options)
                .Select(option => option.EmulatorName)
                .ToArray();

            Assert.Contains("DuckStation", profileNames);
            Assert.Contains("RetroArch", profileNames);
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaFact]
    public void SettingsWindow_OffersNoDestructivePackActionInTheTextureSection()
    {
        var viewModel = CreateSettingsViewModel();
        viewModel.SelectedSection = SettingsSection.TexturePacks;
        var window = new EmulatorSettingsWindow { DataContext = viewModel };
        window.Show();
        try
        {
            Dispatcher.UIThread.RunJobs();

            var labels = window.GetVisualDescendants()
                .OfType<Button>()
                .Select(button => button.Content as string)
                .Where(content => content is not null)
                .ToArray();

            Assert.DoesNotContain(labels, label =>
                label!.Contains("Delete", StringComparison.OrdinalIgnoreCase) ||
                label.Contains("Remove", StringComparison.OrdinalIgnoreCase) ||
                label.Contains("Install", StringComparison.OrdinalIgnoreCase) ||
                label.Contains("Repair", StringComparison.OrdinalIgnoreCase));
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaFact]
    public void ListView_ShowsTheTexturesColumnHeaderAndSortsByIt()
    {
        var viewModel = new MainViewModel { IsGridView = false };
        var window = new MainWindow { DataContext = viewModel };
        window.Show();
        try
        {
            Dispatcher.UIThread.RunJobs();

            var headers = window.GetVisualDescendants()
                .OfType<TextBlock>()
                .Select(block => block.Text)
                .ToArray();
            Assert.Contains("TEXTURES", headers);

            viewModel.SortByCommand.Execute(LibrarySortColumn.Textures);
            Assert.Equal(LibrarySortColumn.Textures, viewModel.SortColumn);
        }
        finally
        {
            window.Close();
        }
    }

    private static EmulatorSettingsViewModel CreateSettingsViewModel()
    {
        var snapshot = new TexturePackInventorySnapshot(
            DuckStationDefinition.Instance.Id,
            "duckstation:/textures",
            "/textures",
            DateTimeOffset.UtcNow,
            TexturePackRootStatus.Ready,
            [
                new TexturePackInventoryEntry(
                    "SLUS-00594",
                    "/textures/SLUS-00594",
                    TexturePackContentStatus.Usable,
                    [new TexturePackMatchKey(TexturePackMatchRule.ExactSerial, "SLUS-00594")]),
            ]);
        var map = TexturePackLibraryMap.Build(
            [snapshot],
            new Dictionary<long, IReadOnlyList<GameIdentifier>>
            {
                [7] = [new GameIdentifier(GameIdentifierKind.Serial, "SLUS-00594", "test")],
            });
        var platforms = new[]
        {
            new TexturePackPlatformState(
                "playstation",
                "DuckStation",
                "/textures",
                IsOverridden: false,
                TexturePackRootStatus.Ready,
                IsStale: false,
                TexturePackLoadingStatus.Enabled,
                Diagnostic: null),
        };
        var result = new TexturePackInventoryResult(map, platforms);

        return new EmulatorSettingsViewModel(
            KnownSystems.All,
            KnownEmulators.All,
            KnownSystems.All.ToDictionary(
                system => system.Id,
                _ => (EmulatorConfiguration?)null,
                StringComparer.Ordinal),
            new StubConfigurationStore(),
            new FakeDialogService(),
            texturePacks: new TexturePackSettingsContext(
                () => result,
                () => true,
                _ => Task.FromResult(result),
                (_, _) => { },
                new Dictionary<string, string>(StringComparer.Ordinal)
                {
                    ["playstation"] = "Use DuckStation's configured Textures folder, or choose one",
                },
                () => new Dictionary<long, string> { [7] = "Final Fantasy VII" }));
    }

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
