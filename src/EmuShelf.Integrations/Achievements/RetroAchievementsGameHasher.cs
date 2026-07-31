using System.Security.Cryptography;
using System.Text;
using EmuShelf.Core.Achievements;
using EmuShelf.Core.Library;
using EmuShelf.Integrations.Importing;

namespace EmuShelf.Integrations.Achievements;

/// <summary>
/// Read-only canonical hashing compatible with the verified disc algorithms in
/// rcheevos commit 2ac45d357bce2906bb0f1438f3eaf8ce6e78e3c4.
/// Unsupported containers are intentionally not replaced with whole-file MD5.
/// </summary>
public sealed class RetroAchievementsGameHasher : IRetroAchievementsGameHasher
{
    private const string PlayStationId = "playstation";
    private const string PlayStation2Id = "playstation2";
    private const string GameCubeId = "gamecube";
    private const string WiiId = "wii";
    private const string PspId = "psp";
    private const string MegaDriveId = "megadrive";
    private const string NintendoDsId = "nds";
    private const string GameBoyAdvanceId = "gba";
    private const string GameBoyColorId = "gbc";
    private const string SuperNintendoId = "snes";
    private const string DreamcastId = "dreamcast";

    // v2 was the first version that added verified logical-disc readers for GameCube/Wii CISO,
    // WBFS, and RVZ. It was persisted globally before per-system versions existed, so it remains
    // compatible with both current readers. Future reader changes can now invalidate only the
    // affected system.
    private const string LegacyGlobalV2 = "rcheevos-2ac45d3-disc-v2";
    // v4 recognizes sync-stripped Mode 2 CHD frames. It advances only the affected reader.
    private const string PlayStationAlgorithmV4 = "rcheevos-2ac45d3-playstation-v4";
    // GameCube's reader is unchanged, so it keeps the original combined-Nintendo version string;
    // persisted GameCube hashes stay valid and are not needlessly recomputed.
    private const string GameCubeAlgorithm = "rcheevos-2ac45d3-nintendo-v2";
    // The Wii decrypted-partition reader was corrected to scale DOL segment sizes by wii_shift
    // (matching rcheevos); the bump recomputes any hash stored by the earlier, incorrect reader.
    private const string WiiAlgorithmV3 = "rcheevos-2ac45d3-wii-v3";
    // Not suffixed with the container: the hash is PARAM.SFO plus EBOOT.BIN read by logical
    // sector, so adding CHD reads the same bytes and must not invalidate stored ISO/CSO hashes.
    private const string PspAlgorithm = "rcheevos-2ac45d3-psp-v1";
    private const string MegaDriveAlgorithm = "rcheevos-2ac45d3-megadrive-v1";
    private const string NintendoDsAlgorithm = "rcheevos-2ac45d3-nds-v1";
    private const string GameBoyAdvanceAlgorithm = "rcheevos-2ac45d3-gba-v1";
    private const string GameBoyColorAlgorithm = "rcheevos-2ac45d3-gbc-v1";
    private const string SuperNintendoAlgorithm = "rcheevos-2ac45d3-snes-v1";
    // Not suffixed with the container: the hash is IP.BIN plus the boot executable regardless of
    // how the tracks are packaged, so adding CDI or CHD later must not invalidate stored GDI hashes.
    private const string DreamcastAlgorithm = "rcheevos-2ac45d3-dreamcast-v1";

    public string GetAlgorithmVersion(Game game) => game.SystemId switch
    {
        PlayStationId or PlayStation2Id => PlayStationAlgorithmV4,
        GameCubeId => GameCubeAlgorithm,
        WiiId => WiiAlgorithmV3,
        PspId => PspAlgorithm,
        MegaDriveId => MegaDriveAlgorithm,
        NintendoDsId => NintendoDsAlgorithm,
        GameBoyAdvanceId => GameBoyAdvanceAlgorithm,
        GameBoyColorId => GameBoyColorAlgorithm,
        SuperNintendoId => SuperNintendoAlgorithm,
        DreamcastId => DreamcastAlgorithm,
        _ => LegacyGlobalV2,
    };

    public bool IsAlgorithmVersionCompatible(Game game, string persistedVersion)
    {
        if (persistedVersion == GetAlgorithmVersion(game))
            return true;
        // The pre-per-system global version stays valid for readers unchanged since, but the Wii
        // decrypted-partition reader was corrected, so a Wii hash stored under it is recomputed.
        return persistedVersion == LegacyGlobalV2 && game.SystemId is
            PlayStationId or PlayStation2Id or GameCubeId;
    }

    public RetroAchievementsSourceSnapshot Inspect(Game game) =>
        InspectInternal(game).Snapshot;

    public RetroAchievementsHashResult Identify(
        Game game,
        CancellationToken cancellationToken = default)
    {
        var inspected = InspectInternal(game);
        var algorithmVersion = GetAlgorithmVersion(game);
        var attemptedAt = DateTimeOffset.UtcNow;
        if (!inspected.Snapshot.CanHash)
        {
            return new RetroAchievementsHashResult(
                inspected.Snapshot.Status,
                null,
                algorithmVersion,
                inspected.Snapshot.Fingerprint,
                attemptedAt,
                inspected.Snapshot.Error);
        }

        string? hash = null;
        var status = RetroAchievementsIdentificationStatus.Hashed;
        string? error = null;
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            hash = game.SystemId switch
            {
                PlayStationId => PlayStationDiscHasher.Hash(
                    inspected.SourcePath!,
                    isPlayStation2: false,
                    cancellationToken),
                PlayStation2Id => PlayStationDiscHasher.Hash(
                    inspected.SourcePath!,
                    isPlayStation2: true,
                    cancellationToken),
                GameCubeId => GameCubeDiscHasher.Hash(
                    inspected.SourcePath!,
                    cancellationToken),
                WiiId => WiiDiscHasher.Hash(
                    inspected.SourcePath!,
                    cancellationToken),
                PspId => PspDiscHasher.Hash(
                    inspected.SourcePath!,
                    cancellationToken),
                MegaDriveId => HashMegaDrive(
                    inspected.SourcePath!,
                    cancellationToken),
                NintendoDsId => NintendoDsRomHasher.Hash(
                    inspected.SourcePath!,
                    cancellationToken),
                GameBoyAdvanceId => HashGameBoyAdvance(
                    inspected.SourcePath!,
                    cancellationToken),
                GameBoyColorId => HashGameBoyColor(
                    inspected.SourcePath!,
                    cancellationToken),
                SuperNintendoId => HashSuperNintendo(
                    inspected.SourcePath!,
                    cancellationToken),
                DreamcastId => DreamcastDiscHasher.Hash(
                    inspected.SourcePath!,
                    cancellationToken),
                _ => throw new UnsupportedDiscLayoutException(
                    "This system does not have a verified local hash reader."),
            };
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (UnsupportedDiscLayoutException ex)
        {
            status = RetroAchievementsIdentificationStatus.UnsupportedFormat;
            error = ex.Message;
        }
        catch (InvalidDataException ex)
        {
            status = RetroAchievementsIdentificationStatus.InvalidMedia;
            error = ex.Message;
        }
        catch (OverflowException)
        {
            status = RetroAchievementsIdentificationStatus.InvalidMedia;
            error = "The game image contains invalid size or offset data.";
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or
                                   ArgumentException or NotSupportedException)
        {
            status = RetroAchievementsIdentificationStatus.Unreadable;
            error = "The game image or one of its descriptor dependencies could not be read.";
        }

        var after = InspectInternal(game).Snapshot;
        if (!string.Equals(
                inspected.Snapshot.Fingerprint,
                after.Fingerprint,
                StringComparison.Ordinal))
        {
            // Retain the pre-read fingerprint. The next incremental pass will see
            // that it differs from the current source and retry the now-stable file.
            status = RetroAchievementsIdentificationStatus.Unreadable;
            hash = null;
            error = "The game image changed while it was being identified.";
        }

        return new RetroAchievementsHashResult(
            status,
            status == RetroAchievementsIdentificationStatus.Hashed ? hash : null,
            algorithmVersion,
            inspected.Snapshot.Fingerprint,
            attemptedAt,
            error);
    }

    private static InspectedSource InspectInternal(Game game)
    {
        var dependencies = new List<string>();
        string? sourcePath = null;
        var status = RetroAchievementsIdentificationStatus.UnsupportedFormat;
        string? error = null;
        var canHash = false;

        try
        {
            sourcePath = Path.GetFullPath(game.Path);
            dependencies.Add(sourcePath);

            if (game.SystemId is PlayStationId or PlayStation2Id)
            {
                sourcePath = ResolveM3u(sourcePath, dependencies);
                var extension = Path.GetExtension(sourcePath).ToLowerInvariant();
                canHash = extension is ".iso" or ".bin" or ".cue" or ".chd" or ".cso" or ".zso";
                if (extension == ".cue")
                    dependencies.AddRange(CueSheetParser.GetReferencedFiles(sourcePath));

                if (!canHash)
                    error = $"{extension.ToUpperInvariant()} needs a verified logical-disc reader.";
            }
            else if (game.SystemId == GameCubeId)
            {
                var extension = Path.GetExtension(sourcePath).ToLowerInvariant();
                canHash = extension is ".iso" or ".gcm" or ".ciso" or ".wbfs" or ".rvz";
                if (!canHash)
                    error = $"{extension.ToUpperInvariant()} needs a verified logical-disc reader.";
            }
            else if (game.SystemId == WiiId)
            {
                var extension = Path.GetExtension(sourcePath).ToLowerInvariant();
                canHash = extension is ".iso" or ".ciso" or ".wbfs" or ".rvz";
                if (!canHash)
                    error = $"{extension.ToUpperInvariant()} needs a verified logical-disc reader.";
            }
            else if (game.SystemId == PspId)
            {
                var extension = Path.GetExtension(sourcePath).ToLowerInvariant();
                canHash = extension is ".iso" or ".cso" or ".chd";
                if (!canHash)
                    error = $"{extension.ToUpperInvariant()} needs a verified PSP disc reader.";
            }
            else if (game.SystemId == MegaDriveId)
            {
                var extension = Path.GetExtension(sourcePath).ToLowerInvariant();
                canHash = extension is ".md" or ".gen" or ".bin" or ".smd";
                if (!canHash)
                    error = $"{extension.ToUpperInvariant()} needs a verified Mega Drive reader.";
            }
            else if (game.SystemId == NintendoDsId)
            {
                var extension = Path.GetExtension(sourcePath).ToLowerInvariant();
                canHash = extension == ".nds";
                if (!canHash)
                    error = $"{extension.ToUpperInvariant()} needs a verified Nintendo DS reader.";
            }
            else if (game.SystemId == GameBoyAdvanceId)
            {
                var extension = Path.GetExtension(sourcePath).ToLowerInvariant();
                canHash = extension == ".gba";
                if (!canHash)
                    error = $"{extension.ToUpperInvariant()} needs a verified Game Boy Advance reader.";
            }
            else if (game.SystemId == GameBoyColorId)
            {
                var extension = Path.GetExtension(sourcePath).ToLowerInvariant();
                canHash = extension is ".gbc" or ".gb";
                if (!canHash)
                    error = $"{extension.ToUpperInvariant()} needs a verified Game Boy Color reader.";
            }
            else if (game.SystemId == SuperNintendoId)
            {
                var extension = Path.GetExtension(sourcePath).ToLowerInvariant();
                canHash = extension is ".sfc" or ".smc";
                if (!canHash)
                    error = $"{extension.ToUpperInvariant()} needs a verified Super Nintendo reader.";
            }
            else if (game.SystemId == DreamcastId)
            {
                var extension = Path.GetExtension(sourcePath).ToLowerInvariant();
                canHash = DreamcastDisc.IsSupportedExtension(extension) &&
                          DreamcastDisc.TryRecognize(sourcePath);
                if (!canHash)
                    error = DreamcastDisc.IsSupportedExtension(extension)
                        ? "This image does not have a verified Dreamcast data track."
                        : $"{extension.ToUpperInvariant()} needs a verified Dreamcast track reader.";
                if (canHash)
                    dependencies.AddRange(DreamcastDisc.GetReferencedFiles(sourcePath));
            }
            else
            {
                error = "RetroAchievements does not support this EmuShelf system.";
            }

            if (canHash)
            {
                var missingDependency = dependencies.Any(path => !File.Exists(path));
                status = missingDependency
                    ? RetroAchievementsIdentificationStatus.Unreadable
                    : RetroAchievementsIdentificationStatus.NotAttempted;
                if (missingDependency)
                {
                    canHash = false;
                    error = "The game image or one of its descriptor dependencies is missing.";
                }
            }
        }
        catch (InvalidDataException ex)
        {
            status = RetroAchievementsIdentificationStatus.InvalidMedia;
            error = ex.Message;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or
                                   ArgumentException or NotSupportedException)
        {
            status = RetroAchievementsIdentificationStatus.Unreadable;
            error = "The game image or one of its descriptor dependencies could not be inspected.";
        }

        var fingerprint = CreateFingerprint(dependencies.Count > 0 ? dependencies : [game.Path]);
        return new InspectedSource(
            new RetroAchievementsSourceSnapshot(fingerprint, canHash, status, error),
            sourcePath);
    }

    private static string HashMegaDrive(string path, CancellationToken cancellationToken)
    {
        if (MegaDriveRomReader.TryRecognize(path) is null)
        {
            throw new UnsupportedDiscLayoutException(
                "This Mega Drive image is not a supported cartridge layout.");
        }

        // rcheevos hashes the accepted file bytes for Mega Drive. This deliberately does not
        // use the normalized SHA-1 import evidence: an SMD copier layout must match the bytes
        // the RetroAchievements core itself receives.
        return WholeFileRomHasher.Hash(path, cancellationToken);
    }

    private static string HashGameBoyAdvance(string path, CancellationToken cancellationToken)
    {
        if (GameBoyAdvanceRomReader.TryRecognize(path) is not { IsHomebrew: false })
        {
            throw new UnsupportedDiscLayoutException(
                "This Game Boy Advance image is not a supported retail raw cartridge layout.");
        }

        return WholeFileRomHasher.Hash(path, cancellationToken);
    }

    private static string HashGameBoyColor(string path, CancellationToken cancellationToken)
    {
        if (GameBoyColorRomReader.TryRecognize(path) is null)
        {
            throw new UnsupportedDiscLayoutException(
                "This Game Boy Color image is not a supported raw cartridge layout.");
        }

        // rcheevos hashes the whole cartridge file for Game Boy Color, exactly as for GBA.
        return WholeFileRomHasher.Hash(path, cancellationToken);
    }

    private static string HashSuperNintendo(string path, CancellationToken cancellationToken)
    {
        if (SuperNintendoRomReader.TryRecognize(path) is null)
        {
            throw new UnsupportedDiscLayoutException(
                "This Super Nintendo image is not a supported raw cartridge layout.");
        }

        // rcheevos strips only the optional 512-byte copier header and MD5-hashes the rest, which
        // is not the whole-file cartridge hash used for Mega Drive / GBA.
        return SuperNintendoRomHasher.Hash(path, cancellationToken);
    }

    private static string ResolveM3u(string path, ICollection<string> dependencies)
    {
        var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        for (var depth = 0; depth < 8; depth++)
        {
            if (!Path.GetExtension(path).Equals(".m3u", StringComparison.OrdinalIgnoreCase))
                return path;

            if (!visited.Add(path))
                throw new InvalidDataException("The M3U playlist contains a reference cycle.");

            if (!File.Exists(path))
                return path;

            string? reference = null;
            foreach (var line in File.ReadLines(path))
            {
                var value = line.Trim().TrimStart('\uFEFF');
                if (value.Length == 0 || value.StartsWith('#'))
                    continue;

                reference = RemoveMatchingQuotes(value);
                break;
            }

            if (reference is null)
                throw new InvalidDataException("The M3U playlist has no game entry.");

            path = ResolveReference(path, reference);
            dependencies.Add(path);
        }

        throw new InvalidDataException("The M3U playlist nesting is too deep.");
    }

    internal static string ResolveReference(string descriptorPath, string reference)
    {
        if (Path.IsPathRooted(reference))
            return Path.GetFullPath(reference);

        if (Uri.TryCreate(reference, UriKind.Absolute, out var uri))
        {
            if (!uri.IsFile)
                throw new InvalidDataException("Only local descriptor references are supported.");
            return Path.GetFullPath(uri.LocalPath);
        }

        var baseDirectory = Path.GetDirectoryName(Path.GetFullPath(descriptorPath))
            ?? throw new InvalidDataException("The descriptor has no parent directory.");
        var localReference = reference
            .Replace('\\', Path.DirectorySeparatorChar)
            .Replace('/', Path.DirectorySeparatorChar);
        return Path.GetFullPath(Path.Combine(baseDirectory, localReference));
    }

    private static string RemoveMatchingQuotes(string value)
    {
        if (value.Length >= 2 && value[0] == value[^1] && value[0] is '"' or '\'')
            return value[1..^1];
        return value;
    }

    private static string CreateFingerprint(IEnumerable<string> dependencies)
    {
        var description = new StringBuilder();
        foreach (var path in dependencies
                     .Distinct(StringComparer.OrdinalIgnoreCase)
                     .OrderBy(path => path, StringComparer.OrdinalIgnoreCase))
        {
            string fullPath;
            try
            {
                fullPath = Path.GetFullPath(path);
                var info = new FileInfo(fullPath);
                description.Append(fullPath).Append('\0');
                if (info.Exists)
                {
                    description
                        .Append(info.Length).Append(':')
                        .Append(info.LastWriteTimeUtc.Ticks);
                }
                else
                {
                    description.Append("missing");
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or
                                       ArgumentException or NotSupportedException)
            {
                description.Append(path).Append("\0unreadable");
            }
            description.Append('\n');
        }

        return Convert.ToHexString(
                SHA256.HashData(Encoding.UTF8.GetBytes(description.ToString())))
            .ToLowerInvariant();
    }

    private sealed record InspectedSource(
        RetroAchievementsSourceSnapshot Snapshot,
        string? SourcePath);
}

internal sealed class UnsupportedDiscLayoutException : Exception
{
    public UnsupportedDiscLayoutException(string message) : base(message)
    {
    }
}
