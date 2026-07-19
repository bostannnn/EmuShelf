using EmuShelf.Core.Importing;
using EmuShelf.Core.Metadata;
using EmuShelf.Integrations.Importing;
using EmuShelf.Integrations.Systems;

namespace EmuShelf.Infrastructure.Tests.Importing;

public sealed class MegaDriveRomReaderTests : TempAppDirectoryTestBase
{
    private const string FixtureSha1 = "471EE01E97220D35105CC5E9FB2F03765623CD05";
    private readonly FileImportRules _rules = new();

    public MegaDriveRomReaderTests()
    {
        Directory.CreateDirectory(BaseDirectory);
    }

    [Theory]
    [InlineData(".md")]
    [InlineData(".gen")]
    [InlineData(".bin")]
    public void RawRom_RequiresSegaHeaderAndProvidesReadOnlyNormalizedEvidence(string extension)
    {
        var path = WriteRawRom($"Ristar{extension}");
        var beforeBytes = File.ReadAllBytes(path);
        var beforeTimestamp = new DateTime(2026, 7, 19, 14, 30, 0, DateTimeKind.Utc);
        File.SetLastWriteTimeUtc(path, beforeTimestamp);

        var evidence = MegaDriveRomReader.TryRead(path);
        var analysis = _rules.AnalyzeFile(path);
        var metadata = _rules.ReadImportMetadata(path, System("megadrive"));

        Assert.NotNull(evidence);
        Assert.Equal(FixtureSha1, evidence.Sha1);
        Assert.Equal(MegaDriveRomLayout.Raw, evidence.Layout);
        Assert.Equal(GameFileMatch.Compatible, analysis.MatchFor("megadrive"));
        Assert.Contains(analysis.SuggestedSystems, system => system.Id == "megadrive");
        Assert.True(_rules.IsFolderCandidate(path, System("megadrive")));
        Assert.Null(metadata.EmbeddedTitle);
        var identifier = Assert.Single(metadata.Identifiers);
        Assert.Equal(GameIdentifierKind.Sha1, identifier.Kind);
        Assert.Equal(FixtureSha1, identifier.Value);
        Assert.Equal("Mega Drive normalized ROM", identifier.Source);
        Assert.True(identifier.IsPrimary);
        Assert.Equal(beforeBytes, File.ReadAllBytes(path));
        Assert.Equal(beforeTimestamp, File.GetLastWriteTimeUtc(path));
    }

    [Fact]
    public void CopierInterleavedSmd_NormalizesToTheSameChecksumWithoutChangingTheSource()
    {
        var path = WriteInterleavedSmd("Ristar.smd");
        var beforeBytes = File.ReadAllBytes(path);
        var beforeTimestamp = new DateTime(2026, 7, 19, 14, 31, 0, DateTimeKind.Utc);
        File.SetLastWriteTimeUtc(path, beforeTimestamp);

        var evidence = MegaDriveRomReader.TryRead(path);
        var analysis = _rules.AnalyzeFile(path);

        Assert.NotNull(evidence);
        Assert.Equal(FixtureSha1, evidence.Sha1);
        Assert.Equal(MegaDriveRomLayout.CopierInterleaved, evidence.Layout);
        Assert.Equal(GameFileMatch.Compatible, analysis.MatchFor("megadrive"));
        Assert.True(_rules.IsFolderCandidate(path, System("megadrive")));
        Assert.Equal(beforeBytes, File.ReadAllBytes(path));
        Assert.Equal(beforeTimestamp, File.GetLastWriteTimeUtc(path));
    }

    [Fact]
    public void CopierInterleavedSmd_NormalizesEveryPayloadBlock()
    {
        var normalized = new byte[0x8000];
        for (var index = 0; index < normalized.Length; index++)
            normalized[index] = (byte)((index * 31 + 7) & 0xFF);
        "SEGA"u8.CopyTo(normalized.AsSpan(0x100));
        var path = WriteInterleavedSmd("Two blocks.smd", normalized);

        var evidence = MegaDriveRomReader.TryRead(path);

        Assert.NotNull(evidence);
        Assert.Equal("5162331A37D95D9C1625A5CFAAFCA0D6E0504BEC", evidence.Sha1);
        Assert.Equal(MegaDriveRomLayout.CopierInterleaved, evidence.Layout);
    }

    [Fact]
    public void Recognition_RejectsArchivesRawSmdAndFilesWithoutAProvenLayout()
    {
        var archive = WriteRawRom("Ristar.zip");
        var rawSmd = WriteRawRom("Ristar.smd");
        var malformed = Path.Combine(BaseDirectory, "Not a ROM.md");
        File.WriteAllBytes(malformed, new byte[0x4000]);

        Assert.Equal(GameFileMatch.Unsupported, _rules.AnalyzeFile(archive).MatchFor("megadrive"));
        Assert.Equal(GameFileMatch.Incompatible, _rules.AnalyzeFile(rawSmd).MatchFor("megadrive"));
        Assert.Equal(GameFileMatch.Incompatible, _rules.AnalyzeFile(malformed).MatchFor("megadrive"));
        Assert.False(_rules.IsFolderCandidate(archive, System("megadrive")));
        Assert.False(_rules.IsFolderCandidate(rawSmd, System("megadrive")));
        Assert.False(_rules.IsFolderCandidate(malformed, System("megadrive")));
        Assert.Same(GameImportMetadata.Empty, _rules.ReadImportMetadata(malformed, System("megadrive")));
    }

    [Fact]
    public void Recognition_RejectsRomLargerThanTheBoundBeforeHashing()
    {
        var path = Path.Combine(BaseDirectory, "Oversized.md");
        using (var stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None))
        {
            stream.SetLength(MegaDriveRomReader.MaximumNormalizedRomBytes + 2);
            stream.Position = 0x100;
            stream.Write("SEGA"u8);
        }

        Assert.Null(MegaDriveRomReader.TryRead(path));
        Assert.False(_rules.IsFolderCandidate(path, System("megadrive")));
    }

    private string WriteRawRom(string name)
    {
        var bytes = CreateNormalizedRom();
        var path = Path.Combine(BaseDirectory, name);
        File.WriteAllBytes(path, bytes);
        return path;
    }

    private string WriteInterleavedSmd(string name, byte[]? normalized = null)
    {
        normalized ??= CreateNormalizedRom();
        var bytes = new byte[512 + normalized.Length];
        "SMD copier header"u8.CopyTo(bytes);
        for (var blockOffset = 0; blockOffset < normalized.Length; blockOffset += 0x4000)
        {
            for (var index = 0; index < 0x4000 / 2; index++)
            {
                bytes[512 + blockOffset + index] = normalized[blockOffset + (index * 2) + 1];
                bytes[512 + blockOffset + (0x4000 / 2) + index] = normalized[blockOffset + (index * 2)];
            }
        }

        var path = Path.Combine(BaseDirectory, name);
        File.WriteAllBytes(path, bytes);
        return path;
    }

    private static byte[] CreateNormalizedRom()
    {
        var bytes = new byte[0x4000];
        "SEGA"u8.CopyTo(bytes.AsSpan(0x100));
        return bytes;
    }

    private static Core.Systems.GameSystem System(string id) =>
        KnownSystems.All.Single(system => system.Id == id);
}
