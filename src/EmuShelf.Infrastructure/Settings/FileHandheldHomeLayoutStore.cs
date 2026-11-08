using System.Text.Json;
using System.Text.Json.Serialization;
using EmuShelf.Core.Diagnostics;
using EmuShelf.Core.Handheld;

namespace EmuShelf.Infrastructure.Settings;

/// <summary>
/// Atomic, best-effort persistence for the Handheld home layout (portable <c>Settings/handheld-home.json</c>),
/// the same pattern as <see cref="FileSecondScreenDockStore"/>. A missing or damaged file reads as "no layout
/// yet", so home starts from the default; it can never prevent EmuShelf from starting. Slots are stored as
/// one flat list (page × 20 + slot), so a file keeps working if the page size ever changes.
/// </summary>
public sealed class FileHandheldHomeLayoutStore(string filePath, IAppLogger? logger = null) : IHandheldHomeLayoutStore
{
    private const int CurrentVersion = 1;

    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingDefault,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) },
    };

    private readonly object _gate = new();
    private readonly IAppLogger _logger = logger ?? NullAppLogger.Instance;

    public HandheldHomeLayout? Load()
    {
        lock (_gate)
        {
            try
            {
                if (!File.Exists(filePath))
                    return null;
                var document = JsonSerializer.Deserialize<LayoutDocument>(File.ReadAllText(filePath), Options);
                if (document?.Slots is null)
                    return null;
                if (document.Version > CurrentVersion)
                    _logger.Warning($"The Handheld home layout was saved by a newer EmuShelf (version {document.Version}); reading what this one knows.");
                var slots = document.Slots.Select(ToSlot).ToList();
                var folders = (document.Folders ?? [])
                    .Where(folder => !string.IsNullOrWhiteSpace(folder.Id))
                    .Select(folder => new HandheldUserFolder(
                        folder.Id!,
                        string.IsNullOrWhiteSpace(folder.Name) ? "Folder" : folder.Name!,
                        IsColor(folder.Color) ? folder.Color! : HandheldHomeLayout.FolderColors[0],
                        folder.Games ?? [],
                        folder.Sort))
                    .ToList();
                return HandheldHomeLayout.FromSlots(slots, folders);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException or NotSupportedException)
            {
                _logger.Warning($"Could not read the Handheld home layout: {ex.Message}");
                return null;
            }
        }
    }

    public void Save(HandheldHomeLayout layout)
    {
        ArgumentNullException.ThrowIfNull(layout);

        lock (_gate)
        {
            try
            {
                var directory = Path.GetDirectoryName(filePath);
                if (!string.IsNullOrEmpty(directory))
                    Directory.CreateDirectory(directory);

                var slots = layout.AllSlots();
                // Trailing empty slots carry nothing; the minimum page count is the model's to restore.
                var used = slots.ToList().FindLastIndex(slot => !slot.IsEmpty) + 1;
                var document = new LayoutDocument
                {
                    Version = CurrentVersion,
                    Slots = slots.Take(used).Select(ToDocument).ToList(),
                    Folders = layout.Folders.Select(folder => new FolderDocument
                    {
                        Id = folder.Id,
                        Name = folder.Name,
                        Color = folder.Color,
                        Games = folder.GameIds.ToList(),
                        Sort = folder.Sort,
                    }).ToList(),
                };
                var temporaryPath = filePath + ".tmp";
                File.WriteAllText(temporaryPath, JsonSerializer.Serialize(document, Options));
                File.Move(temporaryPath, filePath, overwrite: true);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException or NotSupportedException)
            {
                _logger.Warning($"Could not persist the Handheld home layout: {ex.Message}");
            }
        }
    }

    private static HandheldSlot ToSlot(SlotDocument? slot) => slot?.Kind switch
    {
        HandheldSlotKind.PlatformFolder when !string.IsNullOrEmpty(slot.System) => HandheldSlot.PlatformFolder(slot.System, slot.New),
        HandheldSlotKind.UserFolder when !string.IsNullOrEmpty(slot.Folder) => HandheldSlot.UserFolder(slot.Folder),
        HandheldSlotKind.Game when slot.Game > 0 => HandheldSlot.Game(slot.Game),
        HandheldSlotKind.System when slot.Tile is { } tile && Enum.IsDefined(tile) => new HandheldSlot(HandheldSlotKind.System, SystemTile: tile),
        _ => HandheldSlot.Empty,
    };

    private static SlotDocument? ToDocument(HandheldSlot slot) => slot.Kind switch
    {
        HandheldSlotKind.Empty => null,
        HandheldSlotKind.PlatformFolder => new SlotDocument { Kind = slot.Kind, System = slot.SystemId, New = slot.IsNew },
        HandheldSlotKind.UserFolder => new SlotDocument { Kind = slot.Kind, Folder = slot.FolderId },
        HandheldSlotKind.Game => new SlotDocument { Kind = slot.Kind, Game = slot.GameId },
        _ => new SlotDocument { Kind = slot.Kind, Tile = slot.SystemTile },
    };

    private static bool IsColor(string? value) =>
        value is { Length: 7 } && value[0] == '#' && value.Skip(1).All(Uri.IsHexDigit);

    private sealed class LayoutDocument
    {
        public int Version { get; set; }
        public List<SlotDocument?>? Slots { get; set; }
        public List<FolderDocument>? Folders { get; set; }
    }

    private sealed class SlotDocument
    {
        public HandheldSlotKind Kind { get; set; }
        public string? System { get; set; }
        public string? Folder { get; set; }
        public long Game { get; set; }
        public HandheldSystemTile? Tile { get; set; }
        public bool New { get; set; }
    }

    private sealed class FolderDocument
    {
        public string? Id { get; set; }
        public string? Name { get; set; }
        public string? Color { get; set; }
        public List<long>? Games { get; set; }
        public HandheldFolderSort Sort { get; set; }
    }
}
