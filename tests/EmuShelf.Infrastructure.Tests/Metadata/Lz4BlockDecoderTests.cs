using System.Text;
using EmuShelf.Integrations.Metadata;

namespace EmuShelf.Infrastructure.Tests.Metadata;

public class Lz4BlockDecoderTests
{
    [Fact]
    public void LiteralsOnlyBlock_RoundTrips()
    {
        var data = Encoding.ASCII.GetBytes("The quick brown fox jumps over the lazy dog.");
        var block = CompressedIsoBuilder.EncodeLz4Literals(data);
        var output = new byte[data.Length];

        Assert.True(Lz4BlockDecoder.TryDecompress(block, output));
        Assert.Equal(data, output);
    }

    [Fact]
    public void Match_CopiesOverlappingBackReference()
    {
        // literal "AB", then a match of length 6 at offset 2 → "ABABABAB".
        byte[] block = [0x22, (byte)'A', (byte)'B', 0x02, 0x00];
        var output = new byte[8];

        Assert.True(Lz4BlockDecoder.TryDecompress(block, output));
        Assert.Equal("ABABABAB", Encoding.ASCII.GetString(output));
    }

    [Fact]
    public void TruncatedLiterals_ReturnsFalse()
    {
        // Token claims two literals but only one byte follows.
        byte[] block = [0x22, (byte)'A'];

        Assert.False(Lz4BlockDecoder.TryDecompress(block, new byte[8]));
    }

    [Fact]
    public void MatchOffsetBeyondOutput_ReturnsFalse()
    {
        // literal "A", then a match referencing offset 5 with only one byte produced.
        byte[] block = [0x10, (byte)'A', 0x05, 0x00];

        Assert.False(Lz4BlockDecoder.TryDecompress(block, new byte[8]));
    }
}
