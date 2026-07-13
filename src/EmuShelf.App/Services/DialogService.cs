using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Platform.Storage;
using EmuShelf.App.Views;
using EmuShelf.Core.Systems;

namespace EmuShelf.App.Services;

/// <summary>
/// Avalonia implementation of <see cref="IDialogService"/>. Resolves the main window
/// from the desktop lifetime so it doesn't need to be wired up after construction.
/// </summary>
public sealed class DialogService : IDialogService
{
    private readonly IClassicDesktopStyleApplicationLifetime _lifetime;

    public DialogService(IClassicDesktopStyleApplicationLifetime lifetime)
    {
        _lifetime = lifetime;
    }

    private Window? Owner => _lifetime.MainWindow;

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

    public async Task<GameSystem?> PickSystemAsync(IReadOnlyList<GameSystem> systems, GameSystem? suggested)
    {
        var owner = Owner;
        if (owner is null)
            return null;

        var dialog = new SystemPickerWindow(systems, suggested);
        return await dialog.ShowDialog<GameSystem?>(owner);
    }
}
