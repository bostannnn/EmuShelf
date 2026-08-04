using System.Text;
using EmuShelf.Infrastructure.Build;

namespace EmuShelf.Infrastructure.Tests.Build;

public class EmbeddedSecretsTests
{
    // Mirrors the encoder in src/EmuShelf.Infrastructure/Build/EmbeddedSecrets.targets. Keep the key
    // and transform in sync with both the targets file and EmbeddedSecrets.Key.
    private static readonly byte[] Key = Encoding.UTF8.GetBytes("EmuShelf.embedded.v1");

    [Theory]
    [InlineData("emushelf-dev")]
    [InlineData("p@ss\"word\\test")] // quotes and backslashes must survive the round-trip
    [InlineData("GOCSPX-абвгд-é")] // non-ASCII bytes
    public void Decode_RecoversValueEncodedLikeTheBuildTask(string original)
    {
        var recovered = EmbeddedSecrets.Decode(Encode(original));

        Assert.Equal(original, recovered);
    }

    [Fact]
    public void Decode_ReturnsNullForAnEmptyConstant()
    {
        // The build task writes an empty constant for any environment variable it did not receive.
        Assert.Null(EmbeddedSecrets.Decode(string.Empty));
    }

    [Fact]
    public void Decode_ReturnsNullWhenTheEncodedValueIsBlank()
    {
        Assert.Null(EmbeddedSecrets.Decode(Encode("   ")));
    }

    private static string Encode(string value)
    {
        var bytes = Encoding.UTF8.GetBytes(value);
        for (var i = 0; i < bytes.Length; i++)
            bytes[i] = (byte)(bytes[i] ^ Key[i % Key.Length]);
        return Convert.ToBase64String(bytes);
    }
}
