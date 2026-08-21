using System.Text;
using EmuShelf.Core.SaveSync;
using EmuShelf.Infrastructure.SaveSync;

namespace EmuShelf.Infrastructure.Tests.SaveSync;

public sealed class CloudSaveIndexTests
{
    private static readonly DateTimeOffset Modified = new(2026, 8, 15, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void RoundTrip_PreservesEveryField()
    {
        var snapshots = new[]
        {
            new SaveUnitSnapshot("playstation2/shared/Mcd001.ps2", "hash-a", Modified, "file-card"),
            new SaveUnitSnapshot("playstation/shared/card1", "hash-b", Modified.AddHours(3), null),
        };

        var parsed = CloudSaveIndex.Parse(Encoding.UTF8.GetBytes(CloudSaveIndex.Serialize(snapshots)));

        Assert.Equal(2, parsed.Count);
        Assert.Equal("hash-a", parsed["playstation2/shared/Mcd001.ps2"].ContentHash);
        Assert.Equal("file-card", parsed["playstation2/shared/Mcd001.ps2"].Compatibility);
        Assert.Equal(Modified.AddHours(3), parsed["playstation/shared/card1"].ModifiedUtc);
        Assert.Null(parsed["playstation/shared/card1"].Compatibility);
    }

    [Fact]
    public void Parse_EmptyArray_IsAnEmptyIndexNotAFailure()
    {
        // A remote whose index has been rewritten with nothing left in it is a legitimate state,
        // distinct from an index that could not be read at all.
        Assert.Empty(CloudSaveIndex.Parse("[]"u8));
    }

    [Fact]
    public void Parse_EmptyPayload_Throws()
    {
        // Zero bytes means the read failed or the file is half-written; treating it as "no saves on
        // the remote" would tell every machine its uploads are missing.
        Assert.Throws<InvalidDataException>(() => CloudSaveIndex.Parse(ReadOnlySpan<byte>.Empty));
    }

    [Theory]
    [InlineData("not json at all")]
    [InlineData("""{"UnitId":"a"}""")]
    public void Parse_Malformed_Throws(string json) =>
        Assert.Throws<InvalidDataException>(() => CloudSaveIndex.Parse(Encoding.UTF8.GetBytes(json)));

    [Fact]
    public void Parse_MissingHash_Throws()
    {
        var json = $$"""[{"UnitId":"playstation2/a","ContentHash":"","ModifiedUtc":"{{Modified:O}}"}]""";
        Assert.Throws<InvalidDataException>(() => CloudSaveIndex.Parse(Encoding.UTF8.GetBytes(json)));
    }

    [Fact]
    public void Parse_DefaultTimestamp_Throws()
    {
        var json = """[{"UnitId":"playstation2/a","ContentHash":"h","ModifiedUtc":"0001-01-01T00:00:00+00:00"}]""";
        Assert.Throws<InvalidDataException>(() => CloudSaveIndex.Parse(Encoding.UTF8.GetBytes(json)));
    }

    [Fact]
    public void Parse_DuplicateUnit_Throws()
    {
        var json = $$"""
            [{"UnitId":"playstation2/a","ContentHash":"h1","ModifiedUtc":"{{Modified:O}}"},
             {"UnitId":"playstation2/a","ContentHash":"h2","ModifiedUtc":"{{Modified:O}}"}]
            """;
        Assert.Throws<InvalidDataException>(() => CloudSaveIndex.Parse(Encoding.UTF8.GetBytes(json)));
    }

    [Fact]
    public void Parse_TraversalUnitId_Throws()
    {
        var json = $$"""[{"UnitId":"playstation2/../escape","ContentHash":"h","ModifiedUtc":"{{Modified:O}}"}]""";
        Assert.Throws<InvalidDataException>(() => CloudSaveIndex.Parse(Encoding.UTF8.GetBytes(json)));
    }

    [Theory]
    [InlineData("playstation2/shared/Mcd001.ps2", true)]
    [InlineData("playstation3/savedata/BLES00000-SAVE", true)]
    [InlineData("", false)]
    [InlineData("   ", false)]
    [InlineData("playstation2//double", false)]
    [InlineData("playstation2/../escape", false)]
    [InlineData("playstation2/./here", false)]
    [InlineData("playstation2/back\\slash", false)]
    [InlineData("remote:name", false)]
    public void IsSafeUnitId_MatchesRemotePathRules(string unitId, bool expected) =>
        Assert.Equal(expected, CloudSaveIndex.IsSafeUnitId(unitId));

    [Fact]
    public void ValidateUnitId_ThrowsArgumentExceptionForUnsafeId() =>
        Assert.Throws<ArgumentException>(() => CloudSaveIndex.ValidateUnitId("playstation2/../escape"));

    [Fact]
    public void PayloadName_AppendsTheSuffix() =>
        Assert.Equal("playstation2/shared/Mcd001.ps2.payload", CloudSaveIndex.PayloadName("playstation2/shared/Mcd001.ps2"));
}
