using System.Buffers.Binary;
using System.Text;
using EmuShelf.Core.Library;
using EmuShelf.Core.Metadata;
using EmuShelf.Integrations.Metadata;

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
        var libretroUri = Assert.Single(libretro.GetCandidates(identifiers, match)).SourceUri;
        Assert.Equal("thumbnails.libretro.com", libretroUri.Host);
        Assert.Contains("PlayStation%202/Named_Boxarts/", libretroUri.AbsoluteUri);
        Assert.EndsWith(".png", libretroUri.AbsolutePath);
    }

    private static Game NewGame(string systemId, string path) => new()
    {
        SystemId = systemId,
        Path = path,
        Title = Path.GetFileNameWithoutExtension(path),
        DateAdded = DateTimeOffset.UtcNow,
    };
}
