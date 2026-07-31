using System.Buffers.Binary;
using System.Text;
using EmuShelf.Core.Importing;
using EmuShelf.Core.Library;
using YamlDotNet.Core;
using YamlDotNet.RepresentationModel;

namespace EmuShelf.Integrations.Emulators.Rpcs3;

/// <summary>
/// Reads the current RPCS3 <c>games.yml</c> title-id-to-path mapping. Version 1 intentionally
/// accepts no inferred folders or alternate cache formats: unsupported input leaves the library
/// untouched so users can update the adapter rather than import a guessed catalogue.
/// </summary>
public sealed class Rpcs3LibrarySource : IExternalLibrarySource
{
    public const int SupportedFormatVersion = 1;
    public const string SourceId = "rpcs3-library";
    private const string GameListFileName = "games.yml";
    private const int MaximumParamSfoBytes = 1024 * 1024;
    private const uint ParamSfoMagic = 0x46535000;
    private const ushort ParamSfoStringFormat = 0x0204;

    private readonly string _configurationDirectory;

    public Rpcs3LibrarySource(string configurationDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(configurationDirectory);
        _configurationDirectory = Path.GetFullPath(configurationDirectory);
        Source = new ExternalLibrarySource(
            SourceId,
            "playstation3",
            "RPCS3 library",
            _configurationDirectory);
    }

    public ExternalLibrarySource Source { get; }

    /// <summary>
    /// Returns the directory that holds RPCS3's <c>games.yml</c> for an already-configured RPCS3
    /// executable, or null when the list is not found there. Recent portable RPCS3 builds keep
    /// the list under <c>config</c>; older portable layouts keep it beside <c>rpcs3.exe</c>.
    /// </summary>
    public static string? LocateConfigurationDirectory(string? executablePath)
    {
        if (string.IsNullOrWhiteSpace(executablePath))
            return null;

        string? executableDirectory;
        try
        {
            executableDirectory = Path.GetDirectoryName(Path.GetFullPath(executablePath));
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return null;
        }

        if (executableDirectory is null)
            return null;

        var configurationDirectory = Path.Combine(executableDirectory, "config");
        if (File.Exists(Path.Combine(configurationDirectory, GameListFileName)))
            return configurationDirectory;

        return File.Exists(Path.Combine(executableDirectory, GameListFileName))
            ? executableDirectory
            : null;
    }

    public Task<IReadOnlyList<ExternalLibraryGameEntry>> ReadGamesAsync(
        CancellationToken cancellationToken = default) =>
        Task.Run(() => ReadGames(cancellationToken), cancellationToken);

    private IReadOnlyList<ExternalLibraryGameEntry> ReadGames(CancellationToken cancellationToken)
    {
        var gameListPath = Path.Combine(_configurationDirectory, GameListFileName);
        if (!File.Exists(gameListPath))
        {
            throw new Rpcs3LibraryFormatException(
                $"The selected folder does not contain RPCS3's {GameListFileName}. " +
                "Select the RPCS3 configuration folder that contains that file.");
        }

        try
        {
            using var stream = new FileStream(
                gameListPath,
                FileMode.Open,
                FileAccess.Read,
                FileShare.ReadWrite);
            using var reader = new StreamReader(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
            var yaml = new YamlStream();
            yaml.Load(reader);
            return ParseGameList(yaml, cancellationToken);
        }
        catch (YamlException ex)
        {
            throw new Rpcs3LibraryFormatException(
                $"RPCS3's {GameListFileName} is not a readable version {SupportedFormatVersion} game list. " +
                "No games were imported.",
                ex);
        }
        catch (IOException ex)
        {
            throw new Rpcs3LibraryFormatException(
                $"Could not read RPCS3's {GameListFileName}: {ex.Message}",
                ex);
        }
        catch (UnauthorizedAccessException ex)
        {
            throw new Rpcs3LibraryFormatException(
                $"EmuShelf is not allowed to read RPCS3's {GameListFileName}: {ex.Message}",
                ex);
        }
    }

    private IReadOnlyList<ExternalLibraryGameEntry> ParseGameList(
        YamlStream yaml,
        CancellationToken cancellationToken)
    {
        if (yaml.Documents.Count == 0 ||
            (yaml.Documents.Count == 1 &&
             yaml.Documents[0].RootNode is YamlScalarNode { Value: null }))
        {
            // RPCS3 accepts a newly created, blank games.yml as an empty map. It is a valid
            // source result, not a format change, so reconciliation can mark prior records
            // source-missing without guessing at any folders.
            return [];
        }

        if (yaml.Documents.Count != 1 || yaml.Documents[0].RootNode is not YamlMappingNode mapping)
        {
            throw new Rpcs3LibraryFormatException(
                $"RPCS3's {GameListFileName} is not the supported version {SupportedFormatVersion} " +
                "title-id-to-path mapping. No games were imported.");
        }

        var entries = new List<ExternalLibraryGameEntry>(mapping.Children.Count);
        foreach (var pair in mapping.Children)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (pair.Key is not YamlScalarNode { Value: { } titleId } ||
                pair.Value is not YamlScalarNode { Value: { } gamePath } ||
                !IsTitleId(titleId) ||
                !IsAbsolutePath(gamePath))
            {
                throw new Rpcs3LibraryFormatException(
                    $"RPCS3's {GameListFileName} contains an unsupported version {SupportedFormatVersion} " +
                    "entry. No games were imported.");
            }

            entries.Add(CreateEntry(titleId, gamePath));
        }

        return entries;
    }

    private static ExternalLibraryGameEntry CreateEntry(string titleId, string gamePath)
    {
        var title = GetFallbackTitle(gamePath, titleId);
        var titleOrigin = GameTitleOrigin.Filename;
        if (TryReadParameterSfo(gamePath, out var metadata) &&
            string.Equals(metadata.TitleId, titleId, StringComparison.Ordinal) &&
            !string.IsNullOrWhiteSpace(metadata.Title))
        {
            title = metadata.Title;
            titleOrigin = GameTitleOrigin.Embedded;
        }

        return new ExternalLibraryGameEntry(
            titleId,
            gamePath,
            title,
            File.Exists(gamePath) || Directory.Exists(gamePath),
            titleOrigin);
    }

    private static bool TryReadParameterSfo(string gamePath, out ParameterSfoMetadata metadata)
    {
        metadata = default;
        var parameterSfo = FindParameterSfo(gamePath);
        if (parameterSfo is null)
            return false;

        try
        {
            using var stream = new FileStream(
                parameterSfo,
                FileMode.Open,
                FileAccess.Read,
                FileShare.ReadWrite);
            if (stream.Length is < 20 or > MaximumParamSfoBytes)
                return false;

            var bytes = new byte[checked((int)stream.Length)];
            stream.ReadExactly(bytes);
            return TryParseParameterSfo(bytes, out metadata);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or
                                   ArgumentException or NotSupportedException or OverflowException)
        {
            return false;
        }
    }

    private static string? FindParameterSfo(string gamePath)
    {
        if (!Directory.Exists(gamePath))
            return null;

        var direct = Path.Combine(gamePath, "PARAM.SFO");
        if (File.Exists(direct))
            return direct;

        var disc = Path.Combine(gamePath, "PS3_GAME", "PARAM.SFO");
        return File.Exists(disc) ? disc : null;
    }

    private static bool TryParseParameterSfo(
        ReadOnlySpan<byte> bytes,
        out ParameterSfoMetadata metadata)
    {
        metadata = default;
        if (bytes.Length < 20 || BinaryPrimitives.ReadUInt32LittleEndian(bytes) != ParamSfoMagic)
            return false;

        var keyTableOffset = BinaryPrimitives.ReadUInt32LittleEndian(bytes[8..]);
        var dataTableOffset = BinaryPrimitives.ReadUInt32LittleEndian(bytes[12..]);
        var entryCount = BinaryPrimitives.ReadUInt32LittleEndian(bytes[16..]);
        if (entryCount > 4096 || keyTableOffset > bytes.Length || dataTableOffset > bytes.Length ||
            20L + (entryCount * 16L) > bytes.Length)
        {
            return false;
        }

        string? titleId = null;
        string? title = null;
        for (var index = 0; index < entryCount; index++)
        {
            var entryOffset = checked(20 + ((int)index * 16));
            var keyOffset = BinaryPrimitives.ReadUInt16LittleEndian(bytes[entryOffset..]);
            var format = BinaryPrimitives.ReadUInt16LittleEndian(bytes[(entryOffset + 2)..]);
            var dataLength = BinaryPrimitives.ReadUInt32LittleEndian(bytes[(entryOffset + 4)..]);
            var dataOffset = BinaryPrimitives.ReadUInt32LittleEndian(bytes[(entryOffset + 12)..]);
            var keyStart = (ulong)keyTableOffset + keyOffset;
            var dataStart = (ulong)dataTableOffset + dataOffset;
            var dataEnd = dataStart + dataLength;
            if (keyStart >= dataTableOffset || dataStart > (ulong)bytes.Length ||
                dataEnd > (ulong)bytes.Length)
            {
                return false;
            }

            var key = ReadNullTerminatedUtf8(
                bytes[(int)keyStart..(int)dataTableOffset]);
            if (format != ParamSfoStringFormat || key is not ("TITLE_ID" or "TITLE"))
                continue;

            var valueStart = checked((int)dataStart);
            var value = ReadNullTerminatedUtf8(bytes.Slice(valueStart, (int)dataLength));
            if (key == "TITLE_ID")
                titleId = value;
            else
                title = value;
        }

        if (!IsTitleId(titleId))
            return false;

        metadata = new ParameterSfoMetadata(titleId!, title?.Trim());
        return true;
    }

    private static string ReadNullTerminatedUtf8(ReadOnlySpan<byte> bytes)
    {
        var length = bytes.IndexOf((byte)0);
        return Encoding.UTF8.GetString(length < 0 ? bytes : bytes[..length]);
    }

    private static bool IsTitleId(string? value)
    {
        if (value is not { Length: 9 })
            return false;

        return value[..4].All(character => character is >= 'A' and <= 'Z') &&
               value[4..].All(char.IsAsciiDigit);
    }

    private static bool IsAbsolutePath(string value) =>
        Path.IsPathFullyQualified(value) ||
        value.StartsWith("\\\\", StringComparison.Ordinal) ||
        (value.Length >= 3 && char.IsAsciiLetter(value[0]) && value[1] == ':' &&
         value[2] is '\\' or '/');

    private static string GetFallbackTitle(string gamePath, string titleId)
    {
        var path = gamePath.TrimEnd('/', '\\');
        var fileName = Path.GetFileName(path) ?? string.Empty;
        if (fileName.Equals("PS3_GAME", StringComparison.OrdinalIgnoreCase))
            fileName = Path.GetFileName(Path.GetDirectoryName(path) ?? string.Empty) ?? string.Empty;
        if (fileName.Length == 0)
            return titleId;

        return Path.HasExtension(fileName)
            ? Path.GetFileNameWithoutExtension(fileName)
            : fileName;
    }

    private readonly record struct ParameterSfoMetadata(string TitleId, string? Title);
}

/// <summary>Raised when the user-selected RPCS3 list is not the explicitly supported format.</summary>
public sealed class Rpcs3LibraryFormatException : Exception
{
    public Rpcs3LibraryFormatException(string message) : base(message)
    {
    }

    public Rpcs3LibraryFormatException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
