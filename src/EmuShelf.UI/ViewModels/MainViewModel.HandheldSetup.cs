using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using EmuShelf.App.Services;
using EmuShelf.Core.Importing;
using EmuShelf.Core.Launching;
using EmuShelf.Core.Settings;
using EmuShelf.Core.Systems;
using EmuShelf.Integrations.Emulators.Android;

namespace EmuShelf.App.ViewModels;

// Handheld mode's Big Type setup (M47 B14): what its questions need from the app. The emulators are the ones Settings and a launch
// would use; a folder is imported as Add games imports it (scan, remember, reconcile, metadata), counting quietly for the Stage.
public partial class MainViewModel
{
    private sealed partial class HandheldLibrary
    {
        public Task<IReadOnlyList<HandheldSetupEmulator>> DescribeEmulatorsAsync(IReadOnlyList<string> systemIds) =>
            Task.Run<IReadOnlyList<HandheldSetupEmulator>>(() => systemIds.Select(DescribeSetupEmulator).ToList());

        private HandheldSetupEmulator DescribeSetupEmulator(string systemId)
        {
            var name = DescribeEmulator(systemId);
            if (name is null || !OperatingSystem.IsAndroid() || EmuShelf.App.App.InstalledPackageProbe is not { } installed ||
                !AndroidEmulatorChoiceCatalog.BySystem.TryGetValue(systemId, out var choices) || choices.Count == 0)
            {
                return new HandheldSetupEmulator(systemId, name, null);
            }
            EmulatorConfiguration? configuration;
            try
            {
                configuration = owner._emulatorConfigurations.Get(systemId);
            }
            catch (Exception)
            {
                configuration = null;
            }
            var chosen = (configuration is null ? null
                : choices.FirstOrDefault(choice => choice.Matches(configuration.EmulatorId, configuration.CorePath))) ?? choices[0];
            return new HandheldSetupEmulator(systemId, chosen.DisplayName, IsAndroidEmulatorChoiceInstalled(chosen, installed));
        }

        public Task<HandheldSetupImport> ImportFolderCountingAsync(string folder, string systemId, Action<int> found) =>
            owner._systemsById.TryGetValue(systemId, out var system)
                ? owner.ImportFolderForSetupAsync(folder, system, found)
                : Task.FromResult(new HandheldSetupImport(0, "That system is not available."));
    }

    /// <summary>
    /// Add games' import for one folder (<see cref="ImportFolderForSystemAsync"/>'s steps), told through <paramref name="found"/> and
    /// the result instead of the status line: Big Type's setup says the count and the outcome on the Stage itself.
    /// </summary>
    private async Task<HandheldSetupImport> ImportFolderForSetupAsync(string folder, GameSystem system, Action<int> found)
    {
        if (system.Id == "playstation3")
            return new HandheldSetupImport(0, "PlayStation 3 games are imported only from RPCS3. Use Settings to sync its library.");
        if (IsBusy)
            return new HandheldSetupImport(0, "Library work is already in progress.");
        IsBusy = true;
        try
        {
            var progress = new Progress<ScanProgress>(p => found(p.CandidatesFound));
            var selection = await _scanner.ScanAsync(folder, system, progress);
            await Task.Run(() => _library.AddLibraryFolder(system.Id, folder));
            var importResult = await ReconcileImportAsync(system, selection);
            await ShowSystemAsync(system);
            await MaybeStartMetadataForImportAsync(importResult.AddedGameIds);
            return new HandheldSetupImport(importResult.AddedCount, null);
        }
        catch (Exception ex)
        {
            _logger.Error($"Setup's folder scan failed for system {system.Id}.", ex);
            return new HandheldSetupImport(0, ex.Message);
        }
        finally
        {
            IsBusy = false;
        }
    }
}
