using System.Globalization;
using EmuShelf.Core.Library;
using EmuShelf.Core.Metadata;

namespace EmuShelf.Integrations.Importing;

/// <summary>GameNative's frontend .steam export: a positive decimal app id, never a command.</summary>
public sealed class SteamShortcutReader : IGameIdentifierExtractor
{
    public static int? TryRead(string path)
    {
        if (!Path.GetExtension(path).Equals(".steam", StringComparison.OrdinalIgnoreCase))
            return null;
        try
        {
            using var stream = File.OpenRead(path);
            if (stream.Length > 64) return null;
            using var reader = new StreamReader(stream);
            // Bound the read too: an export can change after the length check.
            Span<char> buffer = stackalloc char[65];
            var count = reader.ReadBlock(buffer);
            if (count > 64) return null;
            var value = new string(buffer[..count]).Trim();
            return value.Length > 0 && value.All(c => c is >= '0' and <= '9') &&
                int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out var id) && id > 0
                ? id : null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            return null;
        }
    }

    public IReadOnlyList<GameIdentifier> Extract(Game game) => Identifiers(game.Path);

    public static IReadOnlyList<GameIdentifier> Identifiers(string path) => TryRead(path) is { } id
        ? [new(GameIdentifierKind.SteamAppId, id.ToString(CultureInfo.InvariantCulture), "GameNative Steam export", IsPrimary: true)]
        : [];
}
