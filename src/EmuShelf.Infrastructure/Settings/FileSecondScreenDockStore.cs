using System.Text.Json;
using EmuShelf.Core.Diagnostics;
using EmuShelf.Core.SecondScreen;

namespace EmuShelf.Infrastructure.Settings;

/// <summary>
/// Atomic, best-effort persistence for the Thor companion dock. A missing or damaged file degrades
/// to an empty dock; it can never prevent EmuShelf from starting or launching a game.
/// </summary>
public sealed class FileSecondScreenDockStore(string filePath, IAppLogger? logger = null)
    : ISecondScreenDockStore
{
    private readonly object _gate = new();
    private readonly IAppLogger _logger = logger ?? NullAppLogger.Instance;

    public SecondScreenDock Load()
    {
        lock (_gate)
        {
            try
            {
                if (!File.Exists(filePath))
                    return SecondScreenDock.Empty;

                var document = JsonSerializer.Deserialize<DockDocument>(File.ReadAllText(filePath));
                return new SecondScreenDock(document?.Components ?? []);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
            {
                _logger.Warning($"Could not read the second-screen dock: {ex.Message}");
                return SecondScreenDock.Empty;
            }
        }
    }

    public void Save(SecondScreenDock dock)
    {
        ArgumentNullException.ThrowIfNull(dock);

        lock (_gate)
        {
            try
            {
                var directory = Path.GetDirectoryName(filePath);
                if (!string.IsNullOrEmpty(directory))
                    Directory.CreateDirectory(directory);

                var temporaryPath = filePath + ".tmp";
                var document = new DockDocument(dock.Components.ToArray());
                File.WriteAllText(temporaryPath, JsonSerializer.Serialize(document));
                File.Move(temporaryPath, filePath, overwrite: true);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
            {
                _logger.Warning($"Could not persist the second-screen dock: {ex.Message}");
            }
        }
    }

    private sealed record DockDocument(IReadOnlyList<string?> Components);
}
