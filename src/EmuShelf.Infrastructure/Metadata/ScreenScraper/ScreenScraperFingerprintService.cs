using System.Collections.Concurrent;
using System.Security.Cryptography;
using EmuShelf.Core.Library;
using EmuShelf.Core.Metadata.ScreenScraper;
using EmuShelf.Core.Storage;

namespace EmuShelf.Infrastructure.Metadata.ScreenScraper;

public sealed class ScreenScraperFingerprintService : IScreenScraperFingerprintService
{
    private const int BufferSize = 1024 * 1024;
    private readonly IGameFileFingerprintStore _store;
    private readonly ConcurrentDictionary<long, SemaphoreSlim> _gameGates = new();

    public ScreenScraperFingerprintService(IGameFileFingerprintStore store)
    {
        _store = store;
    }

    public async Task<ScreenScraperFingerprintResult> GetOrComputeAsync(
        Game game,
        ScreenScraperFingerprintProfile profile,
        bool allowCompute,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(game);
        ArgumentNullException.ThrowIfNull(profile);
        if (!string.Equals(game.SystemId, profile.SystemId, StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("The fingerprint profile does not match the game system.", nameof(profile));

        var extension = Path.GetExtension(game.Path);
        if (!profile.WholeFileExtensions.Contains(extension))
        {
            return new ScreenScraperFingerprintResult(
                ScreenScraperFingerprintStatus.UnsupportedFormat,
                null,
                "This game format does not have a safe whole-file ScreenScraper fingerprint rule.");
        }

        var gate = _gameGates.GetOrAdd(game.Id, static _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(cancellationToken);
        try
        {
            return await GetOrComputeCoreAsync(game, allowCompute, cancellationToken);
        }
        finally
        {
            gate.Release();
        }
    }

    private async Task<ScreenScraperFingerprintResult> GetOrComputeCoreAsync(
        Game game,
        bool allowCompute,
        CancellationToken cancellationToken)
    {
        FileInfo source;
        try
        {
            source = new FileInfo(game.Path);
            if (!source.Exists)
                return Failure(ScreenScraperFingerprintStatus.SourceMissing, "The game file is unavailable.");
            if (source.Length <= 0)
                return Failure(ScreenScraperFingerprintStatus.ReadFailed, "The game file is empty.");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
        {
            return Failure(ScreenScraperFingerprintStatus.SourceMissing, "The game file is unavailable.");
        }

        var cached = _store.Get(game.Id, ScreenScraperProvider.Id);
        if (cached is not null && IsCurrent(cached, source))
        {
            return new ScreenScraperFingerprintResult(
                ScreenScraperFingerprintStatus.Cached,
                cached,
                null);
        }

        if (!allowCompute)
        {
            return Failure(
                ScreenScraperFingerprintStatus.ConsentRequired,
                "ScreenScraper needs permission to read this file once and calculate its fingerprint.");
        }

        try
        {
            var initialLength = source.Length;
            var initialLastWrite = source.LastWriteTimeUtc;
            using var sha1 = IncrementalHash.CreateHash(HashAlgorithmName.SHA1);
            using var md5 = IncrementalHash.CreateHash(HashAlgorithmName.MD5);
            var crc32 = new Crc32Accumulator();
            var buffer = new byte[BufferSize];
            await using (var stream = new FileStream(
                source.FullName,
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read,
                BufferSize,
                FileOptions.Asynchronous | FileOptions.SequentialScan))
            {
                while (true)
                {
                    var read = await stream.ReadAsync(buffer, cancellationToken);
                    if (read == 0)
                        break;
                    sha1.AppendData(buffer, 0, read);
                    md5.AppendData(buffer, 0, read);
                    crc32.Append(buffer.AsSpan(0, read));
                }
            }

            source.Refresh();
            if (!source.Exists || source.Length != initialLength || source.LastWriteTimeUtc != initialLastWrite)
            {
                return Failure(
                    ScreenScraperFingerprintStatus.SourceChanged,
                    "The game file changed while its fingerprint was being calculated.");
            }

            var fingerprint = new GameFileFingerprint(
                game.Id,
                ScreenScraperProvider.Id,
                source.FullName,
                ScreenScraperFingerprintScope.WholeFile,
                initialLength,
                new DateTimeOffset(initialLastWrite, TimeSpan.Zero),
                crc32.GetHexString(),
                Convert.ToHexString(md5.GetHashAndReset()),
                Convert.ToHexString(sha1.GetHashAndReset()),
                DateTimeOffset.UtcNow);
            _store.Upsert(fingerprint);
            return new ScreenScraperFingerprintResult(
                ScreenScraperFingerprintStatus.Computed,
                fingerprint,
                null);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or CryptographicException)
        {
            return Failure(
                ScreenScraperFingerprintStatus.ReadFailed,
                "The game file could not be read for fingerprinting.");
        }
    }

    private static bool IsCurrent(GameFileFingerprint cached, FileInfo source) =>
        cached.Scope == ScreenScraperFingerprintScope.WholeFile &&
        string.Equals(
            Path.GetFullPath(cached.SourcePath),
            Path.GetFullPath(source.FullName),
            FilePathComparison.Comparison) &&
        cached.FileSize == source.Length &&
        cached.LastWriteAt.ToUnixTimeMilliseconds() ==
            new DateTimeOffset(source.LastWriteTimeUtc, TimeSpan.Zero).ToUnixTimeMilliseconds();

    private static ScreenScraperFingerprintResult Failure(
        ScreenScraperFingerprintStatus status,
        string error) => new(status, null, error);

    private sealed class Crc32Accumulator
    {
        private static readonly uint[] Table = CreateTable();
        private uint _value = uint.MaxValue;

        public void Append(ReadOnlySpan<byte> bytes)
        {
            foreach (var value in bytes)
                _value = Table[(_value ^ value) & 0xFF] ^ (_value >> 8);
        }

        public string GetHexString() => (~_value).ToString("X8");

        private static uint[] CreateTable()
        {
            var table = new uint[256];
            for (uint index = 0; index < table.Length; index++)
            {
                var value = index;
                for (var bit = 0; bit < 8; bit++)
                    value = (value & 1) != 0 ? 0xEDB88320u ^ (value >> 1) : value >> 1;
                table[index] = value;
            }
            return table;
        }
    }
}
