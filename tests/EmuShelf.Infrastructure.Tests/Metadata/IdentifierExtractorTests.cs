using System.Buffers.Binary;
using System.Text;
using EmuShelf.Core.Library;
using EmuShelf.Core.Metadata;
using EmuShelf.Integrations.Metadata;
using EmuShelf.Infrastructure.Tests.Importing;

namespace EmuShelf.Infrastructure.Tests.Metadata;

public class IdentifierExtractorTests : TempAppDirectoryTestBase
{
    public IdentifierExtractorTests()
    {
        Directory.CreateDirectory(BaseDirectory);
    }

    [Fact]
    public void PlayStationExtractor_ReadsAndNormalizesDiscProductCode()
    {
        var path = Path.Combine(BaseDirectory, "game.iso");
        File.WriteAllText(path, "BOOT2 = cdrom0:\\SLUS_202.65;1\r\nVER = 1.00", Encoding.ASCII);
        var game = NewGame("playstation2", path);

        var identifiers = new PlayStationIdentifierExtractor().Extract(game);

        var identifier = Assert.Single(identifiers);
        Assert.Equal(GameIdentifierKind.Serial, identifier.Kind);
        Assert.Equal("SLUS-20265", identifier.Value);
        Assert.Equal("DiscContent", identifier.Source);
        Assert.True(identifier.IsPrimary);
    }

    [Fact]
    public void PlayStationExtractor_ReadsSerialFromSystemCnf_IgnoringEarlierDecoy()
    {
        // A valid ISO9660 image whose reserved system area contains a decoy product code
        // ahead of SYSTEM.CNF. The targeted read must return the real boot serial, proving
        // it reads SYSTEM.CNF directly instead of linearly scanning the disc.
        var path = Path.Combine(BaseDirectory, "game.iso");
        File.WriteAllBytes(
            path,
            PlayStationIsoBuilder.BuildPlayStation2Iso("SLUS_200.64", decoySerial: "SLES-00001"));
        var game = NewGame("playstation2", path);

        var identifier = Assert.Single(new PlayStationIdentifierExtractor().Extract(game));

        Assert.Equal("SLUS-20064", identifier.Value);
        Assert.Equal("DiscContent", identifier.Source);
        Assert.True(identifier.IsPrimary);
    }

    [Fact]
    public void PlayStationExtractor_FollowsM3uAndCueReferences()
    {
        var disc1 = Path.Combine(BaseDirectory, "disc1.bin");
        var disc2 = Path.Combine(BaseDirectory, "disc2.bin");
        File.WriteAllText(disc1, "BOOT = cdrom:\\SCUS_941.63;1", Encoding.ASCII);
        File.WriteAllText(disc2, "BOOT = cdrom:\\SCUS_944.91;1", Encoding.ASCII);
        var cue1 = Path.Combine(BaseDirectory, "disc1.cue");
        var cue2 = Path.Combine(BaseDirectory, "disc2.cue");
        File.WriteAllText(cue1, "FILE \"disc1.bin\" BINARY\n");
        File.WriteAllText(cue2, "FILE \"disc2.bin\" BINARY\n");
        var playlist = Path.Combine(BaseDirectory, "game.m3u");
        File.WriteAllText(playlist, "disc1.cue\ndisc2.cue\n");

        var identifiers = new PlayStationIdentifierExtractor()
            .Extract(NewGame("playstation", playlist));

        Assert.Equal(["SCUS-94163", "SCUS-94491"], identifiers.Select(item => item.Value));
    }

    [Theory]
    [InlineData("SLUS-00594", "SLUS-00594")]
    [InlineData("SCUS94163", "SCUS-94163")]
    public void PlayStationExtractor_ReadsSerialFromPbpParamSfoDiscId(
        string discId,
        string expected)
    {
        var path = Path.Combine(BaseDirectory, "Some Game.pbp");
        File.WriteAllBytes(path, PbpBuilder.BuildWithDiscId(discId));

        var identifier = Assert.Single(new PlayStationIdentifierExtractor()
            .Extract(NewGame("playstation", path)));

        Assert.Equal(expected, identifier.Value);
        Assert.Equal("DiscContent", identifier.Source);
        Assert.True(identifier.IsPrimary);
    }

    [Fact]
    public void PlayStationExtractor_MalformedPbp_FallsBackToFilenameSerial()
    {
        var path = Path.Combine(BaseDirectory, "Gran Turismo [SCUS-94194].pbp");
        File.WriteAllBytes(path, [1, 2, 3, 4, 5, 6, 7, 8]);

        var identifier = Assert.Single(new PlayStationIdentifierExtractor()
            .Extract(NewGame("playstation", path)));

        Assert.Equal("SCUS-94194", identifier.Value);
        Assert.Equal("Filename", identifier.Source);
    }

    [Fact]
    public void PlayStationExtractor_ReadsSerialFromCsoDeflateImage()
    {
        var iso = PlayStationIsoBuilder.BuildPlayStation2Iso("SLUS_200.64", decoySerial: "SLES-00001");
        var path = Path.Combine(BaseDirectory, "game.cso");
        File.WriteAllBytes(path, CompressedIsoBuilder.BuildCso(iso));

        var identifier = Assert.Single(new PlayStationIdentifierExtractor()
            .Extract(NewGame("playstation2", path)));

        Assert.Equal("SLUS-20064", identifier.Value);
        Assert.Equal("DiscContent", identifier.Source);
    }

    [Fact]
    public void PlayStationExtractor_ReadsSerialFromZsoLz4Image()
    {
        var iso = PlayStationIsoBuilder.BuildPlayStation2Iso("SLUS_200.64", decoySerial: "SLES-00001");
        var path = Path.Combine(BaseDirectory, "game.zso");
        File.WriteAllBytes(path, CompressedIsoBuilder.BuildZso(iso));

        var identifier = Assert.Single(new PlayStationIdentifierExtractor()
            .Extract(NewGame("playstation2", path)));

        Assert.Equal("SLUS-20064", identifier.Value);
        Assert.Equal("DiscContent", identifier.Source);
    }

    [Fact]
    public void PlayStationExtractor_CorruptCso_FallsBackToFilenameSerial()
    {
        var path = Path.Combine(BaseDirectory, "Ico [SLUS-20495].cso");
        File.WriteAllBytes(path, [1, 2, 3, 4, 5, 6, 7, 8, 9, 10]);

        var identifier = Assert.Single(new PlayStationIdentifierExtractor()
            .Extract(NewGame("playstation2", path)));

        Assert.Equal("SLUS-20495", identifier.Value);
        Assert.Equal("Filename", identifier.Source);
    }

    [Fact]
    public void PlayStationExtractor_UsesExplicitFilenameSerialForCompressedContainer()
    {
        var path = Path.Combine(BaseDirectory, "Gran Turismo [SCUS-94194].chd");
        File.WriteAllText(path, "compressed");

        var identifier = Assert.Single(new PlayStationIdentifierExtractor()
            .Extract(NewGame("playstation", path)));

        Assert.Equal("SCUS-94194", identifier.Value);
        Assert.Equal("Filename", identifier.Source);
    }

    [Fact]
    public void NintendoExtractor_ReadsSixCharacterDiscId()
    {
        var path = Path.Combine(BaseDirectory, "game.iso");
        var header = new byte[0x20];
        "GZLE01"u8.CopyTo(header);
        BinaryPrimitives.WriteUInt32BigEndian(header.AsSpan(0x1C, 4), 0xC2339F3Du);
        File.WriteAllBytes(path, header);

        var identifier = Assert.Single(new NintendoDiscIdentifierExtractor()
            .Extract(NewGame("gamecube", path)));

        Assert.Equal(GameIdentifierKind.DiscId, identifier.Kind);
        Assert.Equal("GZLE01", identifier.Value);
    }

    [Fact]
    public void MegaDriveProfile_ExtractsOnlyTheNormalizedRomSha1AndUsesCanonicalArtwork()
    {
        var path = Path.Combine(BaseDirectory, "Misleading Filename.md");
        var bytes = new byte[0x4000];
        "SEGA"u8.CopyTo(bytes.AsSpan(0x100));
        File.WriteAllBytes(path, bytes);
        var profile = KnownMetadataProfiles.All.Single(item => item.SystemId == "megadrive");

        var identifier = Assert.Single(profile.IdentifierExtractor.Extract(NewGame("megadrive", path)));

        Assert.Equal(GameIdentifierKind.Sha1, profile.CatalogKeyKind);
        Assert.EndsWith(
            "/metadat/no-intro/Sega%20-%20Mega%20Drive%20-%20Genesis.dat",
            profile.CatalogUri.AbsolutePath);
        Assert.Collection(
            profile.ArtworkProviders,
            provider => Assert.Equal("libretro-thumbnails", provider.Id));
        Assert.Equal(GameIdentifierKind.Sha1, identifier.Kind);
        Assert.Equal("471EE01E97220D35105CC5E9FB2F03765623CD05", identifier.Value);
        Assert.Equal("Mega Drive normalized ROM", identifier.Source);
        Assert.True(identifier.IsPrimary);
    }

    [Fact]
    public void NintendoDsProfile_UsesOnlyRawRomSha1ForCataloguesAndUsesCanonicalArtwork()
    {
        var path = Path.Combine(BaseDirectory, "Misleading DS title.nds");
        File.WriteAllBytes(path, NintendoDsRomReaderTests.CreateRomFixture("Header title", "ABCE"));
        var profile = KnownMetadataProfiles.All.Single(item => item.SystemId == "nds");

        var identifiers = profile.IdentifierExtractor.Extract(NewGame("nds", path));

        Assert.Equal(GameIdentifierKind.Sha1, profile.CatalogKeyKind);
        Assert.EndsWith(
            "/metadat/no-intro/Nintendo%20-%20Nintendo%20DS.dat",
            profile.CatalogUri.AbsolutePath);
        Assert.Collection(
            profile.ArtworkProviders,
            provider => Assert.Equal("libretro-thumbnails", provider.Id));
        Assert.Collection(
            identifiers,
            identifier =>
            {
                Assert.Equal(GameIdentifierKind.TitleId, identifier.Kind);
                Assert.Equal("ABCE", identifier.Value);
                Assert.False(identifier.IsPrimary);
            },
            identifier =>
            {
                Assert.Equal(GameIdentifierKind.Sha1, identifier.Kind);
                Assert.True(identifier.IsPrimary);
            });
    }

    [Fact]
    public void GameBoyAdvanceProfile_UsesOnlyRawRomSha1ForCataloguesAndUsesCanonicalArtwork()
    {
        var path = Path.Combine(BaseDirectory, "Misleading GBA title.gba");
        File.WriteAllBytes(path, GameBoyAdvanceRomReaderTests.CreateRomFixture("Header title", "ABCE"));
        var profile = KnownMetadataProfiles.All.Single(item => item.SystemId == "gba");

        var identifiers = profile.IdentifierExtractor.Extract(NewGame("gba", path));

        Assert.Equal(GameIdentifierKind.Sha1, profile.CatalogKeyKind);
        Assert.EndsWith(
            "/metadat/no-intro/Nintendo%20-%20Game%20Boy%20Advance.dat",
            profile.CatalogUri.AbsolutePath);
        Assert.Collection(
            profile.ArtworkProviders,
            provider => Assert.Equal("libretro-thumbnails", provider.Id));
        Assert.Collection(
            identifiers,
            identifier =>
            {
                Assert.Equal(GameIdentifierKind.TitleId, identifier.Kind);
                Assert.Equal("ABCE", identifier.Value);
                Assert.False(identifier.IsPrimary);
            },
            identifier =>
            {
                Assert.Equal(GameIdentifierKind.Sha1, identifier.Kind);
                Assert.True(identifier.IsPrimary);
            });
    }

    [Fact]
    public void SuperNintendoProfile_UsesOnlyHeaderlessRomSha1AndCanonicalArtwork()
    {
        var path = Path.Combine(BaseDirectory, "Misleading SNES title.sfc");
        File.WriteAllBytes(path, SuperNintendoRomReaderTests.CreateRomFixture("HEADER TITLE"));
        var profile = KnownMetadataProfiles.All.Single(item => item.SystemId == "snes");

        var identifier = Assert.Single(profile.IdentifierExtractor.Extract(NewGame("snes", path)));

        Assert.Equal(GameIdentifierKind.Sha1, profile.CatalogKeyKind);
        Assert.EndsWith(
            "/metadat/no-intro/Nintendo%20-%20Super%20Nintendo%20Entertainment%20System.dat",
            profile.CatalogUri.AbsolutePath);
        Assert.Collection(
            profile.ArtworkProviders,
            provider => Assert.Equal("libretro-thumbnails", provider.Id));
        // The SNES header has no reliable game code, so SHA-1 is the sole (primary) identifier.
        Assert.Equal(GameIdentifierKind.Sha1, identifier.Kind);
        Assert.Equal("Super Nintendo ROM", identifier.Source);
        Assert.True(identifier.IsPrimary);
    }

    // Dreamcast is the only Sha1 profile pointed at a Redump catalogue rather than No-Intro. Redump
    // hashes each track file separately, so the profile must offer more than one candidate key.
    [Fact]
    public void DreamcastProfile_UsesDataTrackSha1BeforeIpBinProductNumberAndCanonicalArtwork()
    {
        var profile = KnownMetadataProfiles.All.Single(item => item.SystemId == "dreamcast");

        Assert.Equal(GameIdentifierKind.Sha1, profile.CatalogKeyKind);
        Assert.Equal([GameIdentifierKind.Sha1, GameIdentifierKind.Serial], profile.CatalogKeyKinds);
        Assert.True(profile.ReadRomSerials);
        Assert.EndsWith(
            "/metadat/redump/Sega%20-%20Dreamcast.dat",
            profile.CatalogUri.AbsolutePath);
        Assert.Collection(
            profile.ArtworkProviders,
            provider => Assert.Equal("libretro-thumbnails", provider.Id));
        Assert.IsType<DreamcastGdiIdentifierExtractor>(profile.IdentifierExtractor);
        // An unreadable path yields no evidence rather than a filename guess.
        Assert.Empty(profile.IdentifierExtractor.Extract(
            NewGame("dreamcast", Path.Combine(BaseDirectory, "absent.gdi"))));
    }

    [Fact]
    public void ArtworkProviders_UseSerialThenCanonicalLibretroTitle()
    {
        var identifiers = new[]
        {
            new GameIdentifier(GameIdentifierKind.Serial, "SLUS-20265", "DiscContent", true),
        };
        var xlenore = new XlenoreArtworkProvider(
            "xlenore",
            "https://example.test/covers");
        var libretro = new LibretroArtworkProvider("Sony - PlayStation 2");
        var match = new GameCatalogMatch(
            "libretro",
            "SLUS-20265",
            "007 - Agent Under Fire (USA)",
            "USA");

        Assert.Equal(
            "https://example.test/covers/SLUS-20265.jpg",
            Assert.Single(xlenore.GetCandidates(identifiers, match)).SourceUri.ToString());
        var libretroUri = libretro.GetCandidates(identifiers, match).First().SourceUri;
        Assert.Equal("thumbnails.libretro.com", libretroUri.Host);
        Assert.Contains("PlayStation%202/Named_Boxarts/", libretroUri.AbsoluteUri);
        Assert.EndsWith(".png", libretroUri.AbsolutePath);
    }

    [Fact]
    public void PspProfile_UsesParamSfoSerialAndCanonicalArtwork()
    {
        var path = Path.Combine(BaseDirectory, "Lumines.iso");
        File.WriteAllBytes(path, PspIsoBuilder.Build("ULUS10002", "Lumines"));
        var profile = KnownMetadataProfiles.All.Single(item => item.SystemId == "psp");

        var identifier = Assert.Single(profile.IdentifierExtractor.Extract(NewGame("psp", path)));

        Assert.Equal(GameIdentifierKind.Serial, profile.CatalogKeyKind);
        Assert.EndsWith(
            "/metadat/redump/Sony%20-%20PlayStation%20Portable.dat",
            profile.CatalogUri.AbsolutePath);
        Assert.Equal(GameIdentifierKind.Serial, identifier.Kind);
        Assert.Equal("ULUS-10002", identifier.Value);
        Assert.Equal("PSP PARAM.SFO", identifier.Source);
        Assert.True(identifier.IsPrimary);
        Assert.Equal(
            "https://thumbnails.libretro.com/Sony%20-%20PlayStation%20Portable/Named_Boxarts/" +
            "Lumines%20%28USA%29.png",
            profile.ArtworkProviders.Single().GetCandidates(
                [identifier],
                new GameCatalogMatch("libretro-database", "ULUS-10002", "Lumines (USA)", "USA"))
                .First()
                .SourceUri.AbsoluteUri);
    }

    [Fact]
    public void PlayStation3Profile_UsesRpcs3TitleIdAndCanonicalArtwork()
    {
        var profile = KnownMetadataProfiles.All.Single(item => item.SystemId == "playstation3");
        var game = NewGame("playstation3", Path.Combine(BaseDirectory, "RPCS3", "Demon's Souls")) with
        {
            ExternalSourceId = "rpcs3-library",
            ExternalSourceEntryId = "BLUS30443",
        };

        var identifier = Assert.Single(profile.IdentifierExtractor.Extract(game));

        Assert.Equal(GameIdentifierKind.Serial, profile.CatalogKeyKind);
        Assert.EndsWith(
            "/metadat/redump/Sony%20-%20PlayStation%203.dat",
            profile.CatalogUri.AbsolutePath);
        Assert.Equal(GameIdentifierKind.Serial, identifier.Kind);
        Assert.Equal("BLUS-30443", identifier.Value);
        Assert.Equal("RPCS3 title id", identifier.Source);
        Assert.True(identifier.IsPrimary);
        Assert.Equal(
            "https://thumbnails.libretro.com/Sony%20-%20PlayStation%203/Named_Boxarts/" +
            "Demon%27s%20Souls%20%28USA%29.png",
            profile.ArtworkProviders.Last().GetCandidates(
                [identifier],
                new GameCatalogMatch("libretro-database", "BLUS-30443", "Demon's Souls (USA)", "USA"))
                .First()
                .SourceUri.AbsoluteUri);

        var gameTdbCandidates = profile.ArtworkProviders.First().GetCandidates([identifier], null);
        Assert.Equal(
            "https://art.gametdb.com/ps3/coverHQ/US/BLUS30443.jpg",
            gameTdbCandidates.First().SourceUri.AbsoluteUri);

        // `coverHQ` is a partial set, so the standard-resolution set is probed after every
        // high-resolution region rather than leaving the release without a cover.
        Assert.Contains(
            gameTdbCandidates,
            candidate => candidate.SourceUri.AbsoluteUri ==
                "https://art.gametdb.com/ps3/cover/US/BLUS30443.jpg");
        Assert.True(
            gameTdbCandidates.TakeWhile(candidate =>
                candidate.SourceUri.AbsolutePath.Contains("/coverHQ/", StringComparison.Ordinal)).Any());
    }

    [Theory]
    [InlineData("megadrive", "Sonic The Hedgehog (USA, Europe)", "Sega%20-%20Mega%20Drive%20-%20Genesis")]
    [InlineData("nds", "Mario Kart DS (USA, Australia) (En,Fr,De,Es,It)", "Nintendo%20-%20Nintendo%20DS")]
    [InlineData("gba", "Pokemon - FireRed Version (USA, Europe)", "Nintendo%20-%20Game%20Boy%20Advance")]
    [InlineData("snes", "Super Mario World (USA)", "Nintendo%20-%20Super%20Nintendo%20Entertainment%20System")]
    public void ExpansionArtwork_UsesTitleOnlyAfterAnExactCatalogMatch(
        string systemId,
        string canonicalTitle,
        string expectedPlaylist)
    {
        var profile = KnownMetadataProfiles.All.Single(item => item.SystemId == systemId);
        var provider = Assert.Single(profile.ArtworkProviders);

        Assert.Empty(provider.GetCandidates([], match: null));

        var candidate = provider.GetCandidates(
            [],
            new GameCatalogMatch("libretro-database", "exact-key", canonicalTitle, "USA"))
            .First();
        Assert.Contains(expectedPlaylist, candidate.SourceUri.AbsoluteUri);
        Assert.EndsWith(".png", candidate.SourceUri.AbsolutePath);
    }

    [Theory]
    [InlineData("GALE01", "US", "US,EN")]      // USA GameCube
    [InlineData("RMCJ01", "JA", "JA,EN,US")]   // Japanese Wii
    [InlineData("RMCP01", "EN", "EN,US")]      // PAL Wii
    [InlineData("GXXD01", "DE", "DE,EN,US")]   // German PAL
    public void GameTdbProvider_BuildsDiscIdCoverUrlsWithRegionFallback(
        string discId,
        string primaryFolder,
        string expectedFolders)
    {
        var identifiers = new[]
        {
            new GameIdentifier(GameIdentifierKind.DiscId, discId, "DiscHeader", true),
        };

        var candidates = new GameTdbArtworkProvider().GetCandidates(identifiers, match: null);

        Assert.Equal(
            expectedFolders.Split(','),
            candidates.Select(candidate => candidate.SourceUri.Segments[^2].TrimEnd('/')));
        Assert.Equal(
            $"https://art.gametdb.com/wii/cover/{primaryFolder}/{discId}.png",
            candidates[0].SourceUri.ToString());
        Assert.All(candidates, candidate => Assert.Equal(".png", candidate.FileExtension));
    }

    [Fact]
    public void GameTdbProvider_IgnoresNonDiscIdIdentifiers()
    {
        var identifiers = new[]
        {
            new GameIdentifier(GameIdentifierKind.Serial, "SLUS-20265", "DiscContent", true),
        };

        Assert.Empty(new GameTdbArtworkProvider().GetCandidates(identifiers, match: null));
    }

    private static Game NewGame(string systemId, string path) => new()
    {
        SystemId = systemId,
        Path = path,
        Title = Path.GetFileNameWithoutExtension(path),
        DateAdded = DateTimeOffset.UtcNow,
    };
}
