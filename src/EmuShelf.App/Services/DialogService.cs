using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Platform.Storage;
using EmuShelf.App.ViewModels;
using EmuShelf.App.Views;
using EmuShelf.Core.Diagnostics;
using EmuShelf.Core.Launching;
using EmuShelf.Core.Systems;

namespace EmuShelf.App.Services;

/// <summary>
/// Avalonia implementation of <see cref="IDialogService"/>. Resolves the main window
/// from the desktop lifetime so it doesn't need to be wired up after construction.
/// </summary>
public sealed class DialogService : IDialogService
{
    private readonly IClassicDesktopStyleApplicationLifetime _lifetime;
    private readonly IAppLogger _logger;

    public DialogService(
        IClassicDesktopStyleApplicationLifetime lifetime,
        IAppLogger? logger = null)
    {
        _lifetime = lifetime;
        _logger = logger ?? NullAppLogger.Instance;
    }

    private Window? Owner => _lifetime.MainWindow;
    private Window? _activeDialog;

    private TopLevel? PickerOwner => _activeDialog ?? Owner;

    public async Task<IReadOnlyList<string>> PickGameFilesAsync()
    {
        var owner = Owner;
        if (owner is null)
            return [];

        var files = await owner.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Add games",
            AllowMultiple = true,
        });

        return files
            .Select(f => f.TryGetLocalPath())
            .Where(p => !string.IsNullOrEmpty(p))
            .Select(p => p!)
            .ToList();
    }

    public async Task<string?> PickFolderAsync()
    {
        var owner = Owner;
        if (owner is null)
            return null;

        var folders = await owner.StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
        {
            Title = "Add a games folder",
            AllowMultiple = false,
        });

        return folders.Count > 0 ? folders[0].TryGetLocalPath() : null;
    }

    public async Task<string?> PickEmulatorExecutableAsync(string emulatorName)
    {
        var owner = PickerOwner;
        if (owner is null)
            return null;

        var files = await owner.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = $"Select {emulatorName} executable",
            AllowMultiple = false,
        });

        return files.Count > 0 ? files[0].TryGetLocalPath() : null;
    }

    public async Task<string?> PickCoverImageAsync(string gameTitle)
    {
        var owner = Owner;
        if (owner is null)
            return null;

        var files = await owner.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = $"Choose cover for {gameTitle}",
            AllowMultiple = false,
            FileTypeFilter =
            [
                new FilePickerFileType("Image files")
                {
                    Patterns = ["*.png", "*.jpg", "*.jpeg", "*.webp", "*.bmp"],
                    AppleUniformTypeIdentifiers = ["public.image"],
                    MimeTypes = ["image/*"],
                },
            ],
        });

        return files.Count > 0 ? files[0].TryGetLocalPath() : null;
    }

    public async Task<bool> ConfirmRemoveGameAsync(string gameTitle)
    {
        var owner = Owner;
        if (owner is null)
            return false;

        var viewModel = new RemoveGameViewModel(gameTitle);
        var dialog = new RemoveGameWindow { DataContext = viewModel };
        viewModel.CloseRequested += confirmed => dialog.Close(confirmed);
        return await dialog.ShowDialog<bool>(owner);
    }

    public async Task<GameSystem?> PickSystemAsync(IReadOnlyList<GameSystem> systems, GameSystem? suggested)
    {
        var owner = Owner;
        if (owner is null)
            return null;

        var dialog = new SystemPickerWindow(systems, suggested);
        return await dialog.ShowDialog<GameSystem?>(owner);
    }

    public async Task ShowEmulatorSettingsAsync(
        IReadOnlyList<GameSystem> systems,
        IReadOnlyList<EmulatorDefinition> emulators,
        IEmulatorConfigurationStore configurations,
        LibraryMaintenanceActions maintenance)
    {
        var owner = Owner;
        if (owner is null)
            return;

        var configured = await Task.Run(() => systems.ToDictionary(
            system => system.Id,
            system => configurations.Get(system.Id),
            StringComparer.Ordinal));
        var viewModel = new EmulatorSettingsViewModel(
            systems,
            emulators,
            configured,
            configurations,
            this,
            maintenance,
            _logger);
        var dialog = new EmulatorSettingsWindow { DataContext = viewModel };
        viewModel.CloseRequested += saved => dialog.Close(saved);

        _activeDialog = dialog;
        try
        {
            await dialog.ShowDialog<bool>(owner);
        }
        finally
        {
            _activeDialog = null;
        }
    }
}
