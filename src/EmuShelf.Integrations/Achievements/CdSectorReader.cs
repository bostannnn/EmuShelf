using System.Text;

namespace EmuShelf.Integrations.Achievements;

internal sealed class CdSectorReader : IDisposable, ILogicalSectorReader
{
    private static readonly byte[] SyncPattern =
        [0x00, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0x00];

    private readonly FileStream _stream;
    private readonly long _fileTrackOffset;
    private int _sectorSize;
    private int _sectorHeaderSize;
    private int _trackFirstSector;
    private readonly int _trackPregapSectors;

    private CdSectorReader(
        FileStream stream,
        long fileTrackOffset,
        int trackFirstSector,
        int trackPregapSectors)
    {
        _stream = stream;
        _fileTrackOffset = fileTrackOffset;
        _trackFirstSector = trackFirstSector;
        _trackPregapSectors = trackPregapSectors;
    }

    public int FirstTrackSector => _trackFirstSector + _trackPregapSectors;

    public static CdSectorReader Open(string path)
    {
        if (Path.GetExtension(path).Equals(".cue", StringComparison.OrdinalIgnoreCase))
        {
            var track = CueSheetParser.GetTrackOne(path);
            var stream = OpenRead(track.FilePath);
            var reader = new CdSectorReader(
                stream,
                fileTrackOffset: 0,
                track.FirstSector,
                track.PregapSectors);
            reader.DetermineSectorLayout();
            if (reader._sectorSize == 0)
                reader.ApplyCueMode(track.Mode);
            if (reader._sectorSize == 0)
            {
                reader.Dispose();
                throw new UnsupportedDiscLayoutException(
                    $"CUE track mode {track.Mode} is not supported.");
            }
            return reader;
        }

        var directStream = OpenRead(path);
        var directReader = new CdSectorReader(directStream, 0, 0, 0);
        directReader.DetermineSectorLayout();
        if (directReader._sectorSize == 0)
            directReader.ApplyLengthFallback();
        if (directReader._sectorSize == 0)
        {
            directReader.Dispose();
            throw new InvalidDataException("The CD sector layout could not be determined.");
        }
        return directReader;
    }

    public int ReadSector(uint sector, Span<byte> destination)
    {
        if (destination.Length > 2048 || sector < _trackFirstSector)
            return 0;

        var offset = checked(
            ((long)sector - _trackFirstSector) * _sectorSize +
            _sectorHeaderSize +
            _fileTrackOffset);
        if (offset < 0 || offset >= _stream.Length)
            return 0;

        _stream.Position = offset;
        var total = 0;
        while (total < destination.Length)
        {
            var read = _stream.Read(destination[total..]);
            if (read == 0)
                break;
            total += read;
        }
        return total;
    }

    public void Dispose() => _stream.Dispose();

    private static FileStream OpenRead(string path) =>
        new(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);

    private void DetermineSectorLayout()
    {
        _sectorSize = 0;
        _sectorHeaderSize = 0;
        var tocSector = 16 + _trackPregapSectors;
        Span<byte> header = stackalloc byte[32];

        if (ReadRaw(tocSector * 2352L, header) && header[..12].SequenceEqual(SyncPattern))
        {
            _sectorSize = 2352;
            _sectorHeaderSize = header.Slice(25, 5).SequenceEqual("CD001"u8) ? 24 : 16;
            _trackFirstSector = GetSector(header) - tocSector;
            return;
        }

        if (ReadRaw(tocSector * 2336L, header) && header[..12].SequenceEqual(SyncPattern))
        {
            _sectorSize = 2336;
            _sectorHeaderSize = header.Slice(25, 5).SequenceEqual("CD001"u8) ? 24 : 16;
            _trackFirstSector = GetSector(header) - tocSector;
            return;
        }

        if (ReadRaw(tocSector * 2048L, header) && header.Slice(1, 5).SequenceEqual("CD001"u8))
        {
            _sectorSize = 2048;
            _sectorHeaderSize = 0;
        }
    }

    private bool ReadRaw(long relativeOffset, Span<byte> destination)
    {
        var offset = _fileTrackOffset + relativeOffset;
        if (offset < 0 || offset + destination.Length > _stream.Length)
            return false;

        _stream.Position = offset;
        _stream.ReadExactly(destination);
        return true;
    }

    private static int GetSector(ReadOnlySpan<byte> header)
    {
        static int FromBcd(byte value) => (value >> 4) * 10 + (value & 0x0F);
        return ((FromBcd(header[12]) * 60) + FromBcd(header[13])) * 75 +
               FromBcd(header[14]) - 150;
    }

    private void ApplyLengthFallback()
    {
        if (_stream.Length % 2352 == 0)
        {
            _sectorSize = 2352;
            _sectorHeaderSize = 24;
        }
        else if (_stream.Length % 2048 == 0)
        {
            _sectorSize = 2048;
            _sectorHeaderSize = 0;
        }
        else if (_stream.Length % 2336 == 0)
        {
            _sectorSize = 2336;
            _sectorHeaderSize = 8;
        }
    }

    private void ApplyCueMode(string mode)
    {
        switch (mode.ToUpperInvariant())
        {
            case "MODE2/2352":
                _sectorSize = 2352;
                _sectorHeaderSize = 24;
                break;
            case "MODE1/2048":
                _sectorSize = 2048;
                _sectorHeaderSize = 0;
                break;
            case "MODE2/2336":
                _sectorSize = 2336;
                _sectorHeaderSize = 8;
                break;
            case "MODE1/2352":
                _sectorSize = 2352;
                _sectorHeaderSize = 16;
                break;
        }
    }
}

internal sealed record CueTrackOne(
    string FilePath,
    string Mode,
    int FirstSector,
    int PregapSectors);

internal static class CueSheetParser
{
    public static CueTrackOne GetTrackOne(string cuePath)
    {
        string? currentFile = null;
        string? trackFile = null;
        string? mode = null;
        int? firstIndex = null;
        int? indexOne = null;
        var inTrackOne = false;

        foreach (var line in File.ReadLines(cuePath))
        {
            var value = line.Trim();
            if (TryParseFile(value, out var file))
            {
                currentFile = RetroAchievementsGameHasher.ResolveReference(cuePath, file);
                continue;
            }

            if (value.StartsWith("TRACK", StringComparison.OrdinalIgnoreCase))
            {
                var fields = value.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
                inTrackOne = fields.Length >= 3 &&
                             int.TryParse(fields[1], out var trackNumber) &&
                             trackNumber == 1;
                if (inTrackOne)
                {
                    trackFile = currentFile;
                    mode = fields[2];
                }
                continue;
            }

            if (!inTrackOne || !value.StartsWith("INDEX", StringComparison.OrdinalIgnoreCase))
                continue;

            var indexFields = value.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
            if (indexFields.Length < 3 || !int.TryParse(indexFields[1], out var index))
                continue;

            var sector = ParseSector(indexFields[2]);
            firstIndex ??= sector;
            if (index == 1)
                indexOne = sector;
        }

        if (trackFile is null || mode is null || indexOne is null)
            throw new InvalidDataException("The CUE sheet has no readable track 01.");
        if (!mode.StartsWith("MODE", StringComparison.OrdinalIgnoreCase))
            throw new UnsupportedDiscLayoutException("CUE track 01 is not a data track.");

        var first = firstIndex ?? indexOne.Value;
        return new CueTrackOne(trackFile, mode, first, indexOne.Value - first);
    }

    public static IReadOnlyList<string> GetReferencedFiles(string cuePath)
    {
        var files = new List<string>();
        foreach (var line in File.ReadLines(cuePath))
        {
            if (TryParseFile(line.Trim(), out var file))
                files.Add(RetroAchievementsGameHasher.ResolveReference(cuePath, file));
        }
        return files;
    }

    private static bool TryParseFile(string value, out string file)
    {
        file = string.Empty;
        if (!value.StartsWith("FILE", StringComparison.OrdinalIgnoreCase) ||
            value.Length == 4 || !char.IsWhiteSpace(value[4]))
        {
            return false;
        }

        var remainder = value[4..].TrimStart();
        if (remainder.Length == 0)
            return false;

        if (remainder[0] is '"' or '\'')
        {
            var endQuote = remainder.IndexOf(remainder[0], 1);
            if (endQuote <= 1)
                return false;
            file = remainder[1..endQuote];
            return true;
        }

        var separator = remainder.IndexOfAny([' ', '\t']);
        file = separator > 0 ? remainder[..separator] : remainder;
        return file.Length > 0;
    }

    private static int ParseSector(string value)
    {
        var fields = value.Split(':');
        if (fields.Length != 3 ||
            !int.TryParse(fields[0], out var minutes) ||
            !int.TryParse(fields[1], out var seconds) ||
            !int.TryParse(fields[2], out var frames) ||
            minutes < 0 || seconds is < 0 or >= 60 || frames is < 0 or >= 75)
        {
            throw new InvalidDataException("The CUE sheet contains an invalid INDEX time.");
        }
        return checked(((minutes * 60) + seconds) * 75 + frames);
    }
}
