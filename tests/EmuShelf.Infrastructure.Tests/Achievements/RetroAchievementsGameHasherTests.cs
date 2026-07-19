using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;
using EmuShelf.Core.Achievements;
using EmuShelf.Core.Library;
using EmuShelf.Infrastructure.Tests.Importing;
using EmuShelf.Infrastructure.Tests.Metadata;
using EmuShelf.Integrations.Achievements;
using ZstdSharp;

namespace EmuShelf.Infrastructure.Tests.Achievements;

public class RetroAchievementsGameHasherTests : TempAppDirectoryTestBase
{
    private readonly RetroAchievementsGameHasher _hasher = new();

    [Theory]
    [InlineData("playstation", "rcheevos-2ac45d3-playstation-v3")]
    [InlineData("playstation2", "rcheevos-2ac45d3-playstation-v3")]
    [InlineData("gamecube", "rcheevos-2ac45d3-nintendo-v2")]
    [InlineData("wii", "rcheevos-2ac45d3-wii-v3")]
    public void AlgorithmVersion_IsScopedByHashReader(string systemId, string expectedVersion)
    {
        var game = Game(systemId, Path.Combine(BaseDirectory, "game.iso"));

        Assert.Equal(expectedVersion, _hasher.GetAlgorithmVersion(game));
        // The corrected Wii reader no longer accepts the pre-split global version; every other
        // reader is unchanged and still does.
        Assert.Equal(
            systemId != "wii",
            _hasher.IsAlgorithmVersionCompatible(game, "rcheevos-2ac45d3-disc-v2"));
    }

    [Theory]
    [InlineData("psp", "rcheevos-2ac45d3-psp-v1", 41)]
    [InlineData("megadrive", "rcheevos-2ac45d3-megadrive-v1", 1)]
    [InlineData("nds", "rcheevos-2ac45d3-nds-v1", 18)]
    [InlineData("gba", "rcheevos-2ac45d3-gba-v1", 5)]
    public void ExpansionAlgorithmVersionsAndConsoleMappings_AreScopedToVerifiedReaders(
        string systemId,
        string expectedVersion,
        int expectedConsoleId)
    {
        var game = Game(systemId, Path.Combine(BaseDirectory, "game.bin"));

        Assert.Equal(expectedVersion, _hasher.GetAlgorithmVersion(game));
        Assert.False(_hasher.IsAlgorithmVersionCompatible(game, "rcheevos-2ac45d3-disc-v2"));
        Assert.Equal(expectedConsoleId, RetroAchievementsConsoles.ForSystem(systemId));
    }

    [Fact]
    public void Identify_MegaDriveRawAndSmd_MatchPinnedWholeFileHashesWithoutWriting()
    {
        Directory.CreateDirectory(BaseDirectory);
        var raw = CreateMegaDriveRom();
        var rawPaths = new[]
        {
            Path.Combine(BaseDirectory, "game.md"),
            Path.Combine(BaseDirectory, "game.gen"),
            Path.Combine(BaseDirectory, "game.bin"),
        };
        var smdPath = Path.Combine(BaseDirectory, "game.smd");
        foreach (var rawPath in rawPaths)
            File.WriteAllBytes(rawPath, raw);
        File.WriteAllBytes(smdPath, CreateSmd(raw));
        var timestamp = new DateTime(2026, 7, 19, 18, 0, 0, DateTimeKind.Utc);

        foreach (var path in rawPaths.Append(smdPath))
        {
            File.SetLastWriteTimeUtc(path, timestamp);
            var bytesBefore = SHA256.HashData(File.ReadAllBytes(path));

            var result = _hasher.Identify(Game("megadrive", path));

            Assert.Equal(RetroAchievementsIdentificationStatus.Hashed, result.Status);
            // rcheevos' Mega Drive reader is MD5 over the supplied content bytes. The raw and
            // copier-interleaved forms intentionally differ; import normalization is not an RA hash.
            Assert.Equal(
                path == smdPath ? "11a10730e044b3e4b862ac8a879c7ee7" : "49729cba6655d04c7c412147e5743b4a",
                result.CanonicalHash);
            Assert.Equal(bytesBefore, SHA256.HashData(File.ReadAllBytes(path)));
            Assert.Equal(timestamp, File.GetLastWriteTimeUtc(path));
        }
    }

    [Fact]
    public void Identify_GameBoyAdvanceAndNintendoDs_MatchPinnedReadersWithoutWriting()
    {
        Directory.CreateDirectory(BaseDirectory);
        var gbaPath = Path.Combine(BaseDirectory, "game.gba");
        var ndsPath = Path.Combine(BaseDirectory, "game.nds");
        File.WriteAllBytes(gbaPath, GameBoyAdvanceRomReaderTests.CreateRomFixture("Example GBA", "ABCE"));
        File.WriteAllBytes(ndsPath, NintendoDsRomReaderTests.CreateRomFixture("Example DS", "ABCE"));
        var timestamp = new DateTime(2026, 7, 19, 18, 1, 0, DateTimeKind.Utc);

        foreach (var (path, systemId, expectedHash) in new[]
                 {
                     (gbaPath, "gba", "74cdb526e30e8b28bf5362209a9c3ca6"),
                     (ndsPath, "nds", "76a7f76f7bccd2ee9e85e4e575b451d1"),
                 })
        {
            File.SetLastWriteTimeUtc(path, timestamp);
            var bytesBefore = SHA256.HashData(File.ReadAllBytes(path));

            var result = _hasher.Identify(Game(systemId, path));

            Assert.Equal(RetroAchievementsIdentificationStatus.Hashed, result.Status);
            Assert.Equal(expectedHash, result.CanonicalHash);
            Assert.Equal(bytesBefore, SHA256.HashData(File.ReadAllBytes(path)));
            Assert.Equal(timestamp, File.GetLastWriteTimeUtc(path));
        }
    }

    [Fact]
    public void Identify_NintendoDsCodeRangesOverRcheevosLimitRemainUnknown()
    {
        Directory.CreateDirectory(BaseDirectory);
        var path = Path.Combine(BaseDirectory, "oversized-code.nds");
        File.WriteAllBytes(path, NintendoDsRomReaderTests.CreateRomFixture(
            "Large code",
            "ABCE",
            romBytes: (16 * 1024 * 1024) + 0x4000,
            arm9Size: (8 * 1024 * 1024) + 1,
            arm7Size: 8 * 1024 * 1024));

        var result = _hasher.Identify(Game("nds", path));

        Assert.Equal(RetroAchievementsIdentificationStatus.UnsupportedFormat, result.Status);
        Assert.Null(result.CanonicalHash);
    }

    [Theory]
    [InlineData("nds", ".nds")]
    [InlineData("gba", ".gba")]
    public void Identify_CartridgeHomebrewRemainsUnknown(string systemId, string extension)
    {
        Directory.CreateDirectory(BaseDirectory);
        var path = Path.Combine(BaseDirectory, "homebrew" + extension);
        var bytes = systemId == "nds"
            ? NintendoDsRomReaderTests.CreateRomFixture("Homebrew", "####", homebrew: true)
            : GameBoyAdvanceRomReaderTests.CreateRomFixture("Homebrew", "####");
        File.WriteAllBytes(path, bytes);

        var result = _hasher.Identify(Game(systemId, path));

        Assert.Equal(RetroAchievementsIdentificationStatus.UnsupportedFormat, result.Status);
        Assert.Null(result.CanonicalHash);
    }

    [Theory]
    [InlineData(".iso")]
    [InlineData(".cso")]
    public void Identify_PspIsoAndCso_MatchPinnedDiscHashWithoutWriting(string extension)
    {
        Directory.CreateDirectory(BaseDirectory);
        var eboot = Enumerable.Range(0, 7000).Select(index => (byte)((index * 29 + 7) & 0xFF)).ToArray();
        var iso = PspIsoBuilder.Build("UCUS98653", "Example PSP", eboot);
        var path = Path.Combine(BaseDirectory, "game" + extension);
        File.WriteAllBytes(path, extension == ".cso" ? CompressedIsoBuilder.BuildCso(iso) : iso);
        var timestamp = new DateTime(2026, 7, 19, 18, 2, 0, DateTimeKind.Utc);
        File.SetLastWriteTimeUtc(path, timestamp);
        var bytesBefore = SHA256.HashData(File.ReadAllBytes(path));

        var result = _hasher.Identify(Game("psp", path));

        Assert.Equal(RetroAchievementsIdentificationStatus.Hashed, result.Status);
        Assert.Equal("69bf38176a62c7c8c5544663316ecf73", result.CanonicalHash);
        Assert.Equal(bytesBefore, SHA256.HashData(File.ReadAllBytes(path)));
        Assert.Equal(timestamp, File.GetLastWriteTimeUtc(path));
    }

    [Fact]
    public void Identify_PspImageWithoutAProductSerialRemainsUnknown()
    {
        Directory.CreateDirectory(BaseDirectory);
        var path = Path.Combine(BaseDirectory, "homebrew.iso");
        File.WriteAllBytes(path, PspIsoBuilder.Build(
            discId: null,
            title: "Homebrew",
            eboot: [1, 2, 3, 4]));

        var result = _hasher.Identify(Game("psp", path));

        Assert.Equal(RetroAchievementsIdentificationStatus.UnsupportedFormat, result.Status);
        Assert.Null(result.CanonicalHash);
    }

    [Fact]
    public void Identify_PspImageWithATruncatedEbootSectorRemainsUnknown()
    {
        Directory.CreateDirectory(BaseDirectory);
        var path = Path.Combine(BaseDirectory, "truncated.iso");
        var image = PspIsoBuilder.Build(
            discId: "UCUS98653",
            title: "Example PSP",
            eboot: [0x42]);
        Array.Resize(ref image, (24 * 2048) + 1);
        File.WriteAllBytes(path, image);

        var result = _hasher.Identify(Game("psp", path));

        Assert.Equal(RetroAchievementsIdentificationStatus.InvalidMedia, result.Status);
        Assert.Null(result.CanonicalHash);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Identify_PlayStationCue_MatchesOfficialRcheevosVector_WithoutWriting(
        bool raw2352)
    {
        Directory.CreateDirectory(BaseDirectory);
        var binPath = Path.Combine(BaseDirectory, "game.bin");
        var cuePath = Path.Combine(BaseDirectory, "game.cue");
        var image = CreatePlayStationImage("SLUS_007.45", 0x07D800, isPs2: false);
        if (raw2352)
            image = ConvertTo2352(image, firstSector: 0);
        File.WriteAllBytes(binPath, image);
        File.WriteAllText(
            cuePath,
            $"FILE \"game.bin\" BINARY\n  TRACK 01 MODE1/{(raw2352 ? 2352 : 2048)}\n    INDEX 01 00:00:00\n");
        var timestamp = new DateTime(2026, 7, 13, 12, 0, 0, DateTimeKind.Utc);
        File.SetLastWriteTimeUtc(binPath, timestamp);
        File.SetLastWriteTimeUtc(cuePath, timestamp);
        var bytesBefore = SHA256.HashData(File.ReadAllBytes(binPath));
        var binTimeBefore = File.GetLastWriteTimeUtc(binPath);
        var cueTimeBefore = File.GetLastWriteTimeUtc(cuePath);

        var result = _hasher.Identify(Game("playstation", cuePath));

        Assert.Equal(RetroAchievementsIdentificationStatus.Hashed, result.Status);
        Assert.Equal("db433fb038cde4fb15c144e8c7dea6e3", result.CanonicalHash);
        Assert.Equal(bytesBefore, SHA256.HashData(File.ReadAllBytes(binPath)));
        Assert.Equal(binTimeBefore, File.GetLastWriteTimeUtc(binPath));
        Assert.Equal(cueTimeBefore, File.GetLastWriteTimeUtc(cuePath));
    }

    [Fact]
    public void Identify_PlayStation2Iso_MatchesOfficialRcheevosVector()
    {
        Directory.CreateDirectory(BaseDirectory);
        var path = Path.Combine(BaseDirectory, "game.iso");
        File.WriteAllBytes(path, CreatePlayStationImage("SLUS_200.64", 0x07D800, isPs2: true));

        var result = _hasher.Identify(Game("playstation2", path));

        Assert.Equal(RetroAchievementsIdentificationStatus.Hashed, result.Status);
        Assert.Equal("01a517e4ad72c6c2654d1b839be7579d", result.CanonicalHash);
    }

    [Fact]
    public void Identify_GameCubeIso_MatchesOfficialRcheevosVector()
    {
        Directory.CreateDirectory(BaseDirectory);
        var path = Path.Combine(BaseDirectory, "game.gcm");
        File.WriteAllBytes(path, CreateGameCubeImage(32));

        var result = _hasher.Identify(Game("gamecube", path));

        Assert.Equal(RetroAchievementsIdentificationStatus.Hashed, result.Status);
        Assert.Equal("c7803b704fa43d22d8f6e55f4789cb45", result.CanonicalHash);
    }

    [Theory]
    [InlineData(".ciso")]
    [InlineData(".wbfs")]
    [InlineData(".rvz")]
    public void Identify_GameCubeContainers_MatchOfficialRcheevosVector(string extension)
    {
        Directory.CreateDirectory(BaseDirectory);
        var image = CreateGameCubeImage(32);
        var path = Path.Combine(BaseDirectory, "game" + extension);
        var container = extension switch
        {
            ".ciso" => BuildNintendoCiso(image),
            ".wbfs" => BuildNintendoWbfs(image),
            ".rvz" => BuildNintendoRvz(image, usePacking: true),
            _ => throw new ArgumentOutOfRangeException(nameof(extension)),
        };
        File.WriteAllBytes(path, container);

        var result = _hasher.Identify(Game("gamecube", path));

        Assert.Equal(RetroAchievementsIdentificationStatus.Hashed, result.Status);
        Assert.Equal("c7803b704fa43d22d8f6e55f4789cb45", result.CanonicalHash);
    }

    [Theory]
    [InlineData(".iso")]
    [InlineData(".ciso")]
    [InlineData(".wbfs")]
    public void Identify_EncryptedWiiContainers_SelectTheCanonicalPartitionBytes(string extension)
    {
        Directory.CreateDirectory(BaseDirectory);
        var image = CreateEncryptedWiiImage();
        var path = Path.Combine(BaseDirectory, "game" + extension);
        var container = extension switch
        {
            ".iso" => image,
            ".ciso" => BuildNintendoCiso(image),
            ".wbfs" => BuildNintendoWbfs(image),
            _ => throw new ArgumentOutOfRangeException(nameof(extension)),
        };
        File.WriteAllBytes(path, container);

        var result = _hasher.Identify(Game("wii", path));

        Assert.Equal(RetroAchievementsIdentificationStatus.Hashed, result.Status);
        Assert.Equal("0671ce24e5e643842b6439e726986977", result.CanonicalHash);
    }

    [Fact]
    public void Identify_CompressedAndNintendoImages_LeaveSourceBytesAndTimestampUnchanged()
    {
        Directory.CreateDirectory(BaseDirectory);
        var timestamp = new DateTime(2026, 7, 19, 12, 0, 0, DateTimeKind.Utc);

        var cso = Path.Combine(BaseDirectory, "ps1.cso");
        File.WriteAllBytes(cso, CompressedIsoBuilder.BuildCso(
            CreatePlayStationImage("SLUS_007.45", 0x07D800, isPs2: false)));
        var wii = Path.Combine(BaseDirectory, "wii.iso");
        File.WriteAllBytes(wii, CreateEncryptedWiiImage());

        foreach (var (path, system) in new[] { (cso, "playstation"), (wii, "wii") })
        {
            File.SetLastWriteTimeUtc(path, timestamp);
            var bytesBefore = SHA256.HashData(File.ReadAllBytes(path));

            var result = _hasher.Identify(Game(system, path));

            Assert.Equal(RetroAchievementsIdentificationStatus.Hashed, result.Status);
            Assert.Equal(bytesBefore, SHA256.HashData(File.ReadAllBytes(path)));
            Assert.Equal(timestamp, File.GetLastWriteTimeUtc(path));
        }
    }

    [Fact]
    public void Identify_WiiRvz_ReconstructsTheEncryptedPartitionBytes()
    {
        Directory.CreateDirectory(BaseDirectory);
        var path = Path.Combine(BaseDirectory, "game.rvz");
        File.WriteAllBytes(path, BuildPartitionedWiiRvz());

        var result = _hasher.Identify(Game("wii", path));

        Assert.Equal(RetroAchievementsIdentificationStatus.Hashed, result.Status);
        Assert.Equal("97b7907eb06bfe07780b7b0f666f6a6e", result.CanonicalHash);
    }

    [Fact]
    public void Identify_DecryptedWiiIso_AppendsTheCanonicalPartitionHash()
    {
        Directory.CreateDirectory(BaseDirectory);
        var path = Path.Combine(BaseDirectory, "game.iso");
        File.WriteAllBytes(path, CreateDecryptedWiiImage());

        var result = _hasher.Identify(Game("wii", path));

        Assert.Equal(RetroAchievementsIdentificationStatus.Hashed, result.Status);
        // Cross-checked against rcheevos' own rc_hash_wii on this exact image (decrypted path,
        // wii_shift = 2). The previous value (a4c7fbc3…) was the pre-fix, non-matching output.
        Assert.Equal("219e0c0f06918801444e6a24f6a0214a", result.CanonicalHash);
    }

    [Fact]
    public void Identify_MalformedNintendoRvz_IsUnsupportedFormat()
    {
        Directory.CreateDirectory(BaseDirectory);
        var path = Path.Combine(BaseDirectory, "game.rvz");
        var rvz = BuildNintendoRvz(CreateGameCubeImage(32));
        var rawEntryTableOffset = checked((int)BinaryPrimitives.ReadUInt64BigEndian(rvz.AsSpan(0x48 + 0xB8, 8)));
        rvz[rawEntryTableOffset] ^= 0xFF;
        File.WriteAllBytes(path, rvz);

        var result = _hasher.Identify(Game("gamecube", path));

        Assert.Equal(RetroAchievementsIdentificationStatus.UnsupportedFormat, result.Status);
        Assert.Null(result.CanonicalHash);
    }

    [Fact]
    public void Identify_PlayStationCso_MatchesUncompressedVector()
    {
        Directory.CreateDirectory(BaseDirectory);
        var path = Path.Combine(BaseDirectory, "game.cso");
        var iso = CreatePlayStationImage("SLUS_007.45", 0x07D800, isPs2: false);
        File.WriteAllBytes(path, CompressedIsoBuilder.BuildCso(iso));

        var result = _hasher.Identify(Game("playstation", path));

        Assert.Equal(RetroAchievementsIdentificationStatus.Hashed, result.Status);
        Assert.Equal("db433fb038cde4fb15c144e8c7dea6e3", result.CanonicalHash);
    }

    [Fact]
    public void Identify_PlayStationZso_MatchesUncompressedVector()
    {
        Directory.CreateDirectory(BaseDirectory);
        var path = Path.Combine(BaseDirectory, "game.zso");
        var iso = CreatePlayStationImage("SLUS_007.45", 0x07D800, isPs2: false);
        File.WriteAllBytes(path, CompressedIsoBuilder.BuildZso(iso));

        var result = _hasher.Identify(Game("playstation", path));

        Assert.Equal(RetroAchievementsIdentificationStatus.Hashed, result.Status);
        Assert.Equal("db433fb038cde4fb15c144e8c7dea6e3", result.CanonicalHash);
    }

    [Fact]
    public void Identify_PlayStation2Cso_MatchesUncompressedVector()
    {
        Directory.CreateDirectory(BaseDirectory);
        var path = Path.Combine(BaseDirectory, "game.cso");
        var iso = CreatePlayStationImage("SLUS_200.64", 0x07D800, isPs2: true);
        File.WriteAllBytes(path, CompressedIsoBuilder.BuildCso(iso));

        var result = _hasher.Identify(Game("playstation2", path));

        Assert.Equal(RetroAchievementsIdentificationStatus.Hashed, result.Status);
        Assert.Equal("01a517e4ad72c6c2654d1b839be7579d", result.CanonicalHash);
    }

    // Exact CHD parity, verified only where the reference encoder (chdman) is installed. The
    // CHD reader itself is proven byte-exact against source ISOs in ChdSectorSourceTests; this
    // closes the loop by hashing a real chdman-produced container to the uncompressed vector.
    [Fact]
    public void Identify_PlayStation2Chd_MatchesUncompressedVector_WhenChdmanAvailable()
    {
        var chdman = FindChdman();
        if (chdman is null)
            return;

        Directory.CreateDirectory(BaseDirectory);
        var isoPath = Path.Combine(BaseDirectory, "source.iso");
        var chdPath = Path.Combine(BaseDirectory, "game.chd");
        File.WriteAllBytes(isoPath, CreatePlayStationImage("SLUS_200.64", 0x07D800, isPs2: true));
        RunChdman(chdman, isoPath, chdPath);

        var result = _hasher.Identify(Game("playstation2", chdPath));

        Assert.Equal(RetroAchievementsIdentificationStatus.Hashed, result.Status);
        Assert.Equal("01a517e4ad72c6c2654d1b839be7579d", result.CanonicalHash);
    }

    [Fact]
    public void Identify_PlayStation1DiscUnderPlayStation2_UsesPlayStationFallback()
    {
        Directory.CreateDirectory(BaseDirectory);
        var binPath = Path.Combine(BaseDirectory, "game.bin");
        var cuePath = Path.Combine(BaseDirectory, "game.cue");
        File.WriteAllBytes(binPath, CreatePlayStationImage("SLUS_007.45", 0x07D800, isPs2: false));
        File.WriteAllText(
            cuePath,
            "FILE \"game.bin\" BINARY\n  TRACK 01 MODE1/2048\n    INDEX 01 00:00:00\n");

        var result = _hasher.Identify(Game("playstation2", cuePath));

        Assert.Equal(RetroAchievementsIdentificationStatus.Hashed, result.Status);
        Assert.Equal("db433fb038cde4fb15c144e8c7dea6e3", result.CanonicalHash);
    }

    [Fact]
    public void Identify_PlayStationNoSystemCnf_FallsBackToPsxExe()
    {
        Directory.CreateDirectory(BaseDirectory);
        var binPath = Path.Combine(BaseDirectory, "game.bin");
        var cuePath = Path.Combine(BaseDirectory, "game.cue");
        const int binarySize = 0x12000;
        var sectors = ((binarySize + 2047) / 2048) + 20;
        var image = CreateIso9660(sectors, "HOMEBREW");
        var exe = AddIsoFile(image, "PSX.EXE", contents: null, binarySize);
        "PS-X EXE"u8.CopyTo(exe);
        BitConverter.GetBytes(binarySize - 2048).CopyTo(exe[28..32]);
        File.WriteAllBytes(binPath, image);
        File.WriteAllText(
            cuePath,
            "FILE \"game.bin\" BINARY\n  TRACK 01 MODE1/2048\n    INDEX 01 00:00:00\n");

        var result = _hasher.Identify(Game("playstation", cuePath));

        Assert.Equal(RetroAchievementsIdentificationStatus.Hashed, result.Status);
        Assert.Equal("e494c79a7315be0dc3e8571c45df162c", result.CanonicalHash);
    }

    [Fact]
    public void Identify_PlayStationExtraSlashInBootPath_ResolvesExecutable()
    {
        Directory.CreateDirectory(BaseDirectory);
        var binPath = Path.Combine(BaseDirectory, "game.bin");
        var cuePath = Path.Combine(BaseDirectory, "game.cue");
        var image = CreatePlayStationImage(
            "SLUS_007.45", 0x07D800, isPs2: false, bootPath: "\\SLUS_007.45");
        File.WriteAllBytes(binPath, image);
        File.WriteAllText(
            cuePath,
            "FILE \"game.bin\" BINARY\n  TRACK 01 MODE1/2048\n    INDEX 01 00:00:00\n");

        var result = _hasher.Identify(Game("playstation", cuePath));

        Assert.Equal(RetroAchievementsIdentificationStatus.Hashed, result.Status);
        Assert.Equal("db433fb038cde4fb15c144e8c7dea6e3", result.CanonicalHash);
    }

    [Fact]
    public void Identify_PlayStationExeInSubfolder_ResolvesThroughDirectoryWalk()
    {
        Directory.CreateDirectory(BaseDirectory);
        var binPath = Path.Combine(BaseDirectory, "game.bin");
        var cuePath = Path.Combine(BaseDirectory, "game.cue");
        File.WriteAllBytes(
            binPath,
            CreatePlayStationImage("bin\\SCES_012.37", 0x07D800, isPs2: false));
        File.WriteAllText(
            cuePath,
            "FILE \"game.bin\" BINARY\n  TRACK 01 MODE1/2048\n    INDEX 01 00:00:00\n");

        var result = _hasher.Identify(Game("playstation", cuePath));

        Assert.Equal(RetroAchievementsIdentificationStatus.Hashed, result.Status);
        Assert.Equal("674018e23a4052113665dfb264e9c2fc", result.CanonicalHash);
    }

    private static string? FindChdman()
    {
        foreach (var candidate in new[]
                 {
                     "/opt/homebrew/bin/chdman", "/usr/local/bin/chdman",
                     "/usr/bin/chdman", "chdman",
                 })
        {
            try
            {
                using var probe = System.Diagnostics.Process.Start(
                    new System.Diagnostics.ProcessStartInfo(candidate, "--version")
                    {
                        RedirectStandardOutput = true,
                        RedirectStandardError = true,
                        UseShellExecute = false,
                    });
                if (probe is null)
                    continue;
                probe.WaitForExit(5000);
                return candidate;
            }
            catch
            {
                // Try the next candidate location.
            }
        }
        return null;
    }

    private static void RunChdman(string chdman, string isoPath, string chdPath)
    {
        using var process = System.Diagnostics.Process.Start(
            new System.Diagnostics.ProcessStartInfo(chdman)
            {
                ArgumentList = { "createdvd", "-i", isoPath, "-o", chdPath, "-c", "zlib" },
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
            })!;
        process.WaitForExit();
        if (process.ExitCode != 0)
            throw new InvalidOperationException(
                $"chdman failed ({process.ExitCode}): {process.StandardError.ReadToEnd()}");
    }

    [Fact]
    public void Inspect_M3uAndCue_FingerprintChangesWithSelectedPayloadTimestamp()
    {
        Directory.CreateDirectory(BaseDirectory);
        var binPath = Path.Combine(BaseDirectory, "disc.bin");
        var cuePath = Path.Combine(BaseDirectory, "disc.cue");
        var m3uPath = Path.Combine(BaseDirectory, "game.m3u");
        File.WriteAllBytes(binPath, new byte[2048]);
        File.WriteAllText(cuePath, "FILE \"disc.bin\" BINARY\n  TRACK 01 MODE1/2048\n    INDEX 01 00:00:00\n");
        File.WriteAllText(m3uPath, "# first disc\ndisc.cue\n");
        var beforeTime = new DateTime(2026, 7, 13, 12, 0, 0, DateTimeKind.Utc);
        File.SetLastWriteTimeUtc(binPath, beforeTime);

        var before = _hasher.Inspect(Game("playstation", m3uPath));
        File.SetLastWriteTimeUtc(binPath, beforeTime.AddMinutes(1));
        var after = _hasher.Inspect(Game("playstation", m3uPath));

        Assert.True(before.CanHash);
        Assert.True(after.CanHash);
        Assert.NotEqual(before.Fingerprint, after.Fingerprint);
    }

    [Theory]
    [InlineData("gamecube", ".rvz")]
    [InlineData("wii", ".rvz")]
    [InlineData("playstation3", "")]
    [InlineData("playstation", ".pbp")] // .pbp is cancelled for RA — never matched, always Unknown
    [InlineData("psp", ".zip")]
    [InlineData("megadrive", ".zip")]
    [InlineData("nds", ".zip")]
    [InlineData("gba", ".zip")]
    public void Identify_UnverifiedFormatsRemainUnsupported(
        string systemId,
        string extension)
    {
        Directory.CreateDirectory(BaseDirectory);
        var path = Path.Combine(BaseDirectory, "game" + extension);
        File.WriteAllBytes(path, [1, 2, 3, 4]);

        var result = _hasher.Identify(Game(systemId, path));

        Assert.Equal(RetroAchievementsIdentificationStatus.UnsupportedFormat, result.Status);
        Assert.Null(result.CanonicalHash);
    }

    [Theory]
    [InlineData("playstation", ".chd")]
    [InlineData("playstation2", ".cso")]
    [InlineData("playstation", ".zso")]
    public void Identify_MalformedCompressedContainer_IsUnsupportedFormat(
        string systemId,
        string extension)
    {
        // The container extension is now hashable, but an unreadable header must fall back to
        // UnsupportedFormat rather than crash or a different algorithm.
        Directory.CreateDirectory(BaseDirectory);
        var path = Path.Combine(BaseDirectory, "game" + extension);
        File.WriteAllBytes(path, [1, 2, 3, 4]);

        var result = _hasher.Identify(Game(systemId, path));

        Assert.Equal(RetroAchievementsIdentificationStatus.UnsupportedFormat, result.Status);
        Assert.Null(result.CanonicalHash);
    }

    private static Game Game(string systemId, string path) => new()
    {
        Id = 1,
        SystemId = systemId,
        Path = path,
        Title = "Test",
        DateAdded = DateTimeOffset.UtcNow,
    };

    private static byte[] CreateMegaDriveRom()
    {
        var bytes = new byte[0x4000];
        for (var index = 0; index < bytes.Length; index++)
            bytes[index] = (byte)((index * 17 + 3) & 0xFF);
        "SEGA"u8.CopyTo(bytes.AsSpan(0x100));
        return bytes;
    }

    private static byte[] CreateSmd(byte[] normalized)
    {
        var smd = new byte[512 + normalized.Length];
        "SMD copier header"u8.CopyTo(smd);
        for (var index = 0; index < normalized.Length / 2; index++)
        {
            smd[512 + index] = normalized[(index * 2) + 1];
            smd[512 + (normalized.Length / 2) + index] = normalized[index * 2];
        }
        return smd;
    }

    private static byte[] CreatePlayStationImage(
        string executableName,
        int executableSize,
        bool isPs2,
        string? bootPath = null)
    {
        // bootPath is the path written into SYSTEM.CNF (which may carry extra slashes); the
        // actual file keeps executableName. They differ only for the extra-slash fixture.
        bootPath ??= executableName;
        var sectors = ((executableSize + 2047) / 2048) + 20;
        var image = CreateIso9660(sectors, "TEST");
        var systemCnf = isPs2
            ? $"BOOT2 = cdrom0:\\{bootPath};1\nVER = 1.0\nVMODE = NTSC\n"
            : $"BOOT=cdrom:\\{bootPath};1\nTCB=4\nEVENT=10\nSTACK=801FFFF0\n";
        AddIsoFile(image, "SYSTEM.CNF", Encoding.ASCII.GetBytes(systemCnf));
        var executable = AddIsoFile(image, executableName, contents: null, executableSize);
        if (isPs2)
        {
            "\u007fELF"u8.CopyTo(executable);
        }
        else
        {
            "PS-X EXE"u8.CopyTo(executable);
            var bodySize = executableSize - 2048;
            BitConverter.GetBytes(bodySize).CopyTo(executable[28..32]);
        }
        return image;
    }

    private static byte[] CreateIso9660(int sectors, string label)
    {
        var image = new byte[sectors * 2048];
        var descriptor = image.AsSpan(16 * 2048, 2048);
        new byte[] { 0x01, (byte)'C', (byte)'D', (byte)'0', (byte)'0', (byte)'1', 0x01, 0x00 }
            .CopyTo(descriptor);
        Encoding.ASCII.GetBytes(label).CopyTo(descriptor[40..]);
        descriptor[128] = 0x00;
        descriptor[129] = 0x08;
        descriptor[158] = 17;
        image[17 * 2048 - 4] = 18;
        return image;
    }

    private static Span<byte> AddIsoFile(
        byte[] image,
        string filename,
        byte[]? contents,
        int contentsSize = -1)
    {
        if (contentsSize < 0)
            contentsSize = contents?.Length ?? 0;
        const int rootOffset = 17 * 2048;
        var entryOffset = rootOffset;
        var nextSector = image[rootOffset - 4] |
                         (image[rootOffset - 3] << 8) |
                         (image[rootOffset - 2] << 16);

        // Port of rcheevos generate_iso9660_file: a backslash-separated path walks (or creates)
        // each subdirectory record before adding the file to the final directory's extent. The
        // single-segment case below is byte-identical to the original root-only builder.
        var name = filename.TrimStart('\\');
        int separator;
        while ((separator = name.IndexOf('\\')) >= 0)
        {
            var segment = Encoding.ASCII.GetBytes(name[..separator]);
            var found = false;
            while (image[entryOffset] != 0)
            {
                var isDirectory = image[entryOffset + 25] == 1;
                if (isDirectory &&
                    image[entryOffset + 33 + segment.Length] == 0 &&
                    image.AsSpan(entryOffset + 33, segment.Length).SequenceEqual(segment))
                {
                    entryOffset = (image[entryOffset + 2] | (image[entryOffset + 3] << 8)) * 2048;
                    found = true;
                    break;
                }
                entryOffset += image[entryOffset];
            }
            if (!found)
            {
                image[entryOffset] = (byte)(segment.Length + 48);
                image[entryOffset + 2] = (byte)nextSector;
                image[entryOffset + 3] = (byte)(nextSector >> 8);
                image[entryOffset + 25] = 1; // directory flag
                image[entryOffset + 32] = (byte)segment.Length;
                segment.CopyTo(image.AsSpan(entryOffset + 33));
                image[entryOffset + 33 + segment.Length] = 0;
                entryOffset = nextSector * 2048;
                nextSector++;
            }
            name = name[(separator + 1)..];
        }

        while (image[entryOffset] != 0)
            entryOffset += image[entryOffset];

        var nameBytes = Encoding.ASCII.GetBytes(name);
        image[entryOffset] = (byte)(nameBytes.Length + 48);
        image[entryOffset + 2] = (byte)nextSector;
        image[entryOffset + 3] = (byte)(nextSector >> 8);
        image[entryOffset + 10] = (byte)contentsSize;
        image[entryOffset + 11] = (byte)(contentsSize >> 8);
        image[entryOffset + 12] = (byte)(contentsSize >> 16);
        image[entryOffset + 32] = (byte)(nameBytes.Length + 2);
        nameBytes.CopyTo(image.AsSpan(entryOffset + 33));
        image[entryOffset + 33 + nameBytes.Length] = (byte)';';
        image[entryOffset + 34 + nameBytes.Length] = (byte)'1';

        var file = image.AsSpan(nextSector * 2048, contentsSize);
        if (contents is not null)
            contents.CopyTo(file);
        else
            FillImage(file);

        nextSector += (contentsSize + 2047) / 2048;
        image[rootOffset - 4] = (byte)nextSector;
        image[rootOffset - 3] = (byte)(nextSector >> 8);
        image[rootOffset - 2] = (byte)(nextSector >> 16);
        return file;
    }

    private static byte[] CreateGameCubeImage(int megabytes)
    {
        var image = new byte[megabytes * 1024 * 1024];
        FillImage(image);
        image[0x1C] = 0xC2;
        image[0x1D] = 0x33;
        image[0x1E] = 0x9F;
        image[0x1F] = 0x3D;

        for (var index = 0; index < 8; index++)
            image[0x2440 + 0x14 + index] = (byte)(index % 4 == 3 ? 0xFF : 0);
        for (var index = 0; index < 4; index++)
            image[0x420 + index] = (byte)(index % 4 == 2 ? 0x30 : 0);
        for (var index = 0; index < 18 * 4; index++)
        {
            image[0x3000 + index] = (byte)(index % 4 == 2 ? 0x30 + 1 + index / 4 : 0);
            image[0x3000 + 0x90 + index] = (byte)(index % 8 == 3 ? 0xFF : 0);
        }
        return image;
    }

    private static byte[] CreateEncryptedWiiImage()
    {
        var image = new byte[1024 * 1024];
        FillImage(image);
        image[0x18] = 0x5D;
        image[0x19] = 0x1C;
        image[0x1A] = 0x9E;
        image[0x1B] = 0xA3;
        image[0x61] = 0; // Encrypted Wii disc.

        const int partitionTable = 0x40020;
        const int partitionOffset = 0x50000;
        const int tmdOffset = 0x3000;
        const int dataOffset = 0x80000;
        const int dataSize = 0x10000;
        image.AsSpan(0x40000, 32).Clear();
        BinaryPrimitives.WriteUInt32BigEndian(image.AsSpan(0x40000, 4), 1);
        BinaryPrimitives.WriteUInt32BigEndian(image.AsSpan(0x40004, 4), partitionTable >> 2);
        BinaryPrimitives.WriteUInt32BigEndian(image.AsSpan(partitionTable, 4), partitionOffset >> 2);
        BinaryPrimitives.WriteUInt32BigEndian(image.AsSpan(partitionTable + 4, 4), 0);
        BinaryPrimitives.WriteUInt32BigEndian(image.AsSpan(partitionOffset + 0x2A4, 4), 16);
        BinaryPrimitives.WriteUInt32BigEndian(image.AsSpan(partitionOffset + 0x2A8, 4), tmdOffset >> 2);
        BinaryPrimitives.WriteUInt32BigEndian(image.AsSpan(partitionOffset + 0x2B8, 4), dataOffset >> 2);
        BinaryPrimitives.WriteUInt32BigEndian(image.AsSpan(partitionOffset + 0x2BC, 4), dataSize >> 2);
        FillImage(image.AsSpan(partitionOffset + tmdOffset, 16));
        FillImage(image.AsSpan(dataOffset + 0x400, 0x7C00));
        FillImage(image.AsSpan(dataOffset + 0x8000 + 0x400, 0x7C00));
        return image;
    }

    private static byte[] CreateDecryptedWiiImage()
    {
        var image = CreateEncryptedWiiImage();
        const int dataOffset = 0x80000;
        image[0x61] = 1;
        BinaryPrimitives.WriteUInt32BigEndian(image.AsSpan(0x50000 + 0x2BC, 4), 0x10000 >> 2);
        image.AsSpan(dataOffset, 0x5000).Clear();
        BinaryPrimitives.WriteUInt32BigEndian(image.AsSpan(dataOffset + 0x2440 + 0x14, 4), 0);
        BinaryPrimitives.WriteUInt32BigEndian(image.AsSpan(dataOffset + 0x2440 + 0x18, 4), 0);
        BinaryPrimitives.WriteUInt32BigEndian(image.AsSpan(dataOffset + 0x420, 4), 0xC00);
        var dol = dataOffset + 0x3000;
        BinaryPrimitives.WriteUInt32BigEndian(image.AsSpan(dol, 4), 0xC40);
        BinaryPrimitives.WriteUInt32BigEndian(image.AsSpan(dol + 0x90, 4), 0x40);
        FillImage(image.AsSpan(dataOffset + 0x3100, 0x100));
        return image;
    }

    private static byte[] BuildNintendoCiso(byte[] image)
    {
        const int headerSize = 0x8000;
        const int blockSize = 0x200000;
        var blocks = (image.Length + blockSize - 1) / blockSize;
        var output = new byte[headerSize + blocks * blockSize];
        "CISO"u8.CopyTo(output);
        BinaryPrimitives.WriteUInt32LittleEndian(output.AsSpan(4, 4), blockSize);
        for (var index = 0; index < blocks; index++)
            output[8 + index] = 1;
        image.CopyTo(output.AsSpan(headerSize));
        return output;
    }

    private static byte[] BuildNintendoWbfs(byte[] image)
    {
        const int sectorSize = 0x200000;
        var logicalBlocks = (image.Length + sectorSize - 1) / sectorSize;
        var physicalBlocks = logicalBlocks + 2;
        var output = new byte[physicalBlocks * sectorSize];
        "WBFS"u8.CopyTo(output);
        BinaryPrimitives.WriteUInt32BigEndian(output.AsSpan(4, 4), (uint)physicalBlocks);
        output[8] = 21;
        output[9] = 21;
        output[12] = 1;
        var map = output.AsSpan(sectorSize + 0x100, logicalBlocks * 2);
        for (var index = 0; index < logicalBlocks; index++)
            BinaryPrimitives.WriteUInt16BigEndian(map.Slice(index * 2, 2), (ushort)(index + 2));
        image.CopyTo(output.AsSpan(sectorSize * 2));
        return output;
    }

    private static byte[] BuildNintendoRvz(byte[] image, bool usePacking = false)
    {
        const int header1Size = 0x48;
        const int header2Size = 0xDC;
        const int chunkSize = 0x100000;
        const uint compressionType = 5; // Zstandard, the normal Dolphin RVZ path.
        var groupCount = (image.Length + chunkSize - 1) / chunkSize;
        var rawEntries = new byte[24];
        BinaryPrimitives.WriteUInt64BigEndian(rawEntries.AsSpan(0, 8), 0);
        BinaryPrimitives.WriteUInt64BigEndian(rawEntries.AsSpan(8, 8), (ulong)image.Length);
        BinaryPrimitives.WriteUInt32BigEndian(rawEntries.AsSpan(16, 4), 0);
        BinaryPrimitives.WriteUInt32BigEndian(rawEntries.AsSpan(20, 4), (uint)groupCount);

        using var compressor = new Compressor(5);
        var compressedRawEntries = compressor.Wrap(rawEntries).ToArray();
        var compressedGroups = new byte[groupCount][];
        var packedGroupSizes = new int[groupCount];
        for (var index = 0; index < groupCount; index++)
        {
            var payload = image.AsSpan(index * chunkSize, chunkSize).ToArray();
            if (usePacking)
            {
                var packed = new byte[payload.Length + 4];
                BinaryPrimitives.WriteUInt32BigEndian(packed, (uint)payload.Length);
                payload.CopyTo(packed.AsSpan(4));
                packedGroupSizes[index] = packed.Length;
                payload = packed;
            }
            compressedGroups[index] = compressor.Wrap(payload).ToArray();
        }

        var rawOffset = header1Size + header2Size;
        var groupOffset = Align4(rawOffset + compressedRawEntries.Length);
        var groupTableLength = groupCount * 12;
        var compressedGroupTable = Array.Empty<byte>();
        var dataOffset = 0;
        for (var attempt = 0; attempt < 4; attempt++)
        {
            dataOffset = Align4(groupOffset + (compressedGroupTable.Length == 0
                ? groupTableLength
                : compressedGroupTable.Length));
            var groupTable = new byte[groupTableLength];
            var tableCursor = dataOffset;
            for (var index = 0; index < groupCount; index++)
            {
                var group = compressedGroups[index];
                BinaryPrimitives.WriteUInt32BigEndian(groupTable.AsSpan(index * 12, 4), (uint)(tableCursor >> 2));
                BinaryPrimitives.WriteUInt32BigEndian(groupTable.AsSpan(index * 12 + 4, 4),
                    0x80000000u | (uint)group.Length);
                BinaryPrimitives.WriteUInt32BigEndian(groupTable.AsSpan(index * 12 + 8, 4),
                    (uint)packedGroupSizes[index]);
                tableCursor += Align4(group.Length);
            }

            var nextCompressedTable = compressor.Wrap(groupTable).ToArray();
            if (nextCompressedTable.Length == compressedGroupTable.Length)
            {
                compressedGroupTable = nextCompressedTable;
                break;
            }

            compressedGroupTable = nextCompressedTable;
        }

        dataOffset = Align4(groupOffset + compressedGroupTable.Length);
        var outputSize = dataOffset + compressedGroups.Sum(group => Align4(group.Length));
        var output = new byte[outputSize];
        compressedRawEntries.CopyTo(output.AsSpan(rawOffset));
        compressedGroupTable.CopyTo(output.AsSpan(groupOffset));
        var cursor = dataOffset;
        for (var index = 0; index < groupCount; index++)
        {
            var group = compressedGroups[index];
            group.CopyTo(output.AsSpan(cursor));
            cursor += Align4(group.Length);
        }

        var header2 = output.AsSpan(header1Size, header2Size);
        BinaryPrimitives.WriteUInt32BigEndian(header2.Slice(0, 4), 1); // GameCube
        BinaryPrimitives.WriteUInt32BigEndian(header2.Slice(4, 4), compressionType);
        BinaryPrimitives.WriteUInt32BigEndian(header2.Slice(0x0C, 4), chunkSize);
        image.AsSpan(0, 0x80).CopyTo(header2.Slice(0x10, 0x80));
        BinaryPrimitives.WriteUInt32BigEndian(header2.Slice(0xB4, 4), 1);
        BinaryPrimitives.WriteUInt64BigEndian(header2.Slice(0xB8, 8), (ulong)rawOffset);
        BinaryPrimitives.WriteUInt32BigEndian(header2.Slice(0xC0, 4), (uint)compressedRawEntries.Length);
        BinaryPrimitives.WriteUInt32BigEndian(header2.Slice(0xC4, 4), (uint)groupCount);
        BinaryPrimitives.WriteUInt64BigEndian(header2.Slice(0xC8, 8), (ulong)groupOffset);
        BinaryPrimitives.WriteUInt32BigEndian(header2.Slice(0xD0, 4), (uint)compressedGroupTable.Length);

        var header1 = output.AsSpan(0, header1Size);
        "RVZ\x01"u8.CopyTo(header1);
        BinaryPrimitives.WriteUInt32BigEndian(header1.Slice(4, 4), 0x01000000);
        BinaryPrimitives.WriteUInt32BigEndian(header1.Slice(8, 4), 0x00030000);
        BinaryPrimitives.WriteUInt32BigEndian(header1.Slice(0x0C, 4), header2Size);
        SHA1.HashData(header2).CopyTo(header1.Slice(0x10, 20));
        BinaryPrimitives.WriteUInt64BigEndian(header1.Slice(0x24, 8), (ulong)image.Length);
        BinaryPrimitives.WriteUInt64BigEndian(header1.Slice(0x2C, 8), (ulong)output.Length);
        SHA1.HashData(header1.Slice(0, 0x34)).CopyTo(header1.Slice(0x34, 20));
        return output;
    }

    private static byte[] BuildPartitionedWiiRvz()
    {
        const int header1Size = 0x48;
        const int header2Size = 0xDC;
        const int chunkSize = 0x8000;
        const int partitionDataOffset = 0x80000;
        const int rawSize = partitionDataOffset;
        const int rawGroups = rawSize / chunkSize;
        const int groupCount = rawGroups + 1;
        var rawImage = CreateEncryptedWiiImage();
        BinaryPrimitives.WriteUInt32BigEndian(rawImage.AsSpan(0x50000 + 0x2BC, 4), chunkSize >> 2);

        var partitionOffset = header1Size + header2Size;
        var rawEntryOffset = partitionOffset + 0x30;
        var groupEntryOffset = rawEntryOffset + 24;
        var dataOffset = groupEntryOffset + groupCount * 12;
        var output = new byte[dataOffset + rawSize + 4 + 0x7C00];

        var partitionEntry = output.AsSpan(partitionOffset, 0x30);
        BinaryPrimitives.WriteUInt32BigEndian(partitionEntry.Slice(16, 4), partitionDataOffset / chunkSize);
        BinaryPrimitives.WriteUInt32BigEndian(partitionEntry.Slice(20, 4), 1);
        BinaryPrimitives.WriteUInt32BigEndian(partitionEntry.Slice(24, 4), rawGroups);
        BinaryPrimitives.WriteUInt32BigEndian(partitionEntry.Slice(28, 4), 1);

        var rawEntry = output.AsSpan(rawEntryOffset, 24);
        BinaryPrimitives.WriteUInt64BigEndian(rawEntry.Slice(0, 8), 0);
        BinaryPrimitives.WriteUInt64BigEndian(rawEntry.Slice(8, 8), rawSize);
        BinaryPrimitives.WriteUInt32BigEndian(rawEntry.Slice(20, 4), rawGroups);

        var groupTable = output.AsSpan(groupEntryOffset, groupCount * 12);
        var cursor = dataOffset;
        for (var index = 0; index < rawGroups; index++)
        {
            BinaryPrimitives.WriteUInt32BigEndian(groupTable.Slice(index * 12, 4), (uint)(cursor >> 2));
            BinaryPrimitives.WriteUInt32BigEndian(groupTable.Slice(index * 12 + 4, 4), chunkSize);
            rawImage.AsSpan(index * chunkSize, chunkSize).CopyTo(output.AsSpan(cursor));
            cursor += chunkSize;
        }
        BinaryPrimitives.WriteUInt32BigEndian(groupTable.Slice(rawGroups * 12, 4), (uint)(cursor >> 2));
        BinaryPrimitives.WriteUInt32BigEndian(groupTable.Slice(rawGroups * 12 + 4, 4), 4 + 0x7C00);
        // An empty exception list is two bytes, followed by its required 4-byte alignment.
        // The rest is one 0x7C00-byte decrypted Wii cluster filled with zeroes.

        var header2 = output.AsSpan(header1Size, header2Size);
        BinaryPrimitives.WriteUInt32BigEndian(header2.Slice(0, 4), 2); // Wii
        BinaryPrimitives.WriteUInt32BigEndian(header2.Slice(0x0C, 4), chunkSize);
        rawImage.AsSpan(0, 0x80).CopyTo(header2.Slice(0x10, 0x80));
        BinaryPrimitives.WriteUInt32BigEndian(header2.Slice(0x90, 4), 1);
        BinaryPrimitives.WriteUInt32BigEndian(header2.Slice(0x94, 4), 0x30);
        BinaryPrimitives.WriteUInt64BigEndian(header2.Slice(0x98, 8), (ulong)partitionOffset);
        SHA1.HashData(partitionEntry).CopyTo(header2.Slice(0xA0, 20));
        BinaryPrimitives.WriteUInt32BigEndian(header2.Slice(0xB4, 4), 1);
        BinaryPrimitives.WriteUInt64BigEndian(header2.Slice(0xB8, 8), (ulong)rawEntryOffset);
        BinaryPrimitives.WriteUInt32BigEndian(header2.Slice(0xC0, 4), 24);
        BinaryPrimitives.WriteUInt32BigEndian(header2.Slice(0xC4, 4), groupCount);
        BinaryPrimitives.WriteUInt64BigEndian(header2.Slice(0xC8, 8), (ulong)groupEntryOffset);
        BinaryPrimitives.WriteUInt32BigEndian(header2.Slice(0xD0, 4), (uint)groupTable.Length);

        var header1 = output.AsSpan(0, header1Size);
        "RVZ\x01"u8.CopyTo(header1);
        BinaryPrimitives.WriteUInt32BigEndian(header1.Slice(4, 4), 0x01000000);
        BinaryPrimitives.WriteUInt32BigEndian(header1.Slice(8, 4), 0x00030000);
        BinaryPrimitives.WriteUInt32BigEndian(header1.Slice(0x0C, 4), header2Size);
        SHA1.HashData(header2).CopyTo(header1.Slice(0x10, 20));
        BinaryPrimitives.WriteUInt64BigEndian(header1.Slice(0x24, 8), partitionDataOffset + chunkSize);
        BinaryPrimitives.WriteUInt64BigEndian(header1.Slice(0x2C, 8), (ulong)output.Length);
        SHA1.HashData(header1.Slice(0, 0x34)).CopyTo(header1.Slice(0x34, 20));
        return output;
    }

    private static int Align4(int value) => (value + 3) & ~3;

    private static void FillImage(Span<byte> image)
    {
        var size = image.Length;
        var seed = unchecked((int)(
            (uint)size ^ ((uint)size >> 8) ^ ((uint)(size - 1) * 25387U)));
        var offset = 0;
        while (size > 0)
        {
            int count;
            byte value;
            switch (seed & 0xFF)
            {
                case 0:
                    count = ((seed >> 8) & 0x3F) & ~(size & 0x0F);
                    if (count == 0)
                        count = 1;
                    value = 0;
                    break;
                case 1:
                    count = ((seed >> 8) & 0x07) + 1;
                    value = (byte)(seed >> 16);
                    break;
                case 2:
                    count = ((seed >> 8) & 0x03) + 1;
                    value = (byte)((seed >> 16) ^ 0xFF);
                    break;
                case 3:
                    count = ((seed >> 8) & 0x03) + 1;
                    value = (byte)((seed >> 16) ^ 0xA5);
                    break;
                case 4:
                    count = ((seed >> 8) & 0x03) + 1;
                    value = (byte)((seed >> 16) ^ 0xC3);
                    break;
                case 5:
                    count = ((seed >> 8) & 0x03) + 1;
                    value = (byte)((seed >> 16) ^ 0x96);
                    break;
                case 6:
                case 7:
                    count = ((seed >> 8) & 0x03) + 1;
                    value = (byte)((seed >> 16) ^ 0x78);
                    break;
                default:
                    count = 1;
                    value = (byte)((seed >> 8) ^ (seed >> 16));
                    break;
            }

            do
            {
                image[offset++] = value;
                size--;
            } while (size > 0 && --count > 0);

            seed = unchecked((seed * 0x41C64E6D + 12345) & 0x7FFFFFFF);
        }
    }

    private static byte[] ConvertTo2352(byte[] input, int firstSector)
    {
        var sectors = (input.Length + 2047) / 2048;
        var output = new byte[sectors * 2352];
        ReadOnlySpan<byte> sync = [0x00, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0x00];
        firstSector += 150;
        var frames = firstSector % 75;
        firstSector /= 75;
        var seconds = firstSector % 60;
        var minutes = firstSector / 60;

        for (var index = 0; index < sectors; index++)
        {
            var destination = output.AsSpan(index * 2352, 2352);
            sync.CopyTo(destination);
            destination[12] = ToBcd(minutes);
            destination[13] = ToBcd(seconds);
            destination[14] = ToBcd(frames);
            destination[15] = 2;
            input.AsSpan(index * 2048, 2048).CopyTo(destination[16..]);
            if (++frames == 75)
            {
                frames = 0;
                if (++seconds == 60)
                {
                    seconds = 0;
                    minutes++;
                }
            }
        }
        return output;
    }

    private static byte ToBcd(int value) =>
        (byte)(((value / 10) << 4) | (value % 10));
}
