using EmuShelf.Core.Metadata;
using EmuShelf.Infrastructure.Metadata;

namespace EmuShelf.Infrastructure.Tests.Metadata;

public class LibretroDatCatalogTests
{
    [Fact]
    public void Parser_IndexesTopLevelSerialAndIgnoresNestedRomFields()
    {
        using var reader = new StringReader(
            """
            clrmamepro (
                name "Sony - PlayStation 2"
            )
            game (
                name "007 - Agent Under Fire (USA)"
                region "USA"
                serial "SLUS-20265"
                rom (
                    name "wrong.iso"
                    serial "WRONG-00000"
                )
            )
            """);

        var index = LibretroDatCatalog.Parse(reader, GameIdentifierKind.Serial);

        var entry = Assert.Single(index.Entries).Value;
        Assert.Equal("007 - Agent Under Fire (USA)", entry.Title);
        Assert.Equal("USA", entry.Region);
        Assert.True(index.Entries.ContainsKey("SLUS-20265"));
    }

    [Fact]
    public void Parser_PrefersRetailEntryWhenSerialIsSharedWithBeta()
    {
        using var reader = new StringReader(
            """
            game (
                name "Example Game (USA) (Beta)"
                serial "SLUS_123.45"
            )
            game (
                name "Example Game (USA)"
                serial "SLUS-12345"
            )
            """);

        var index = LibretroDatCatalog.Parse(reader, GameIdentifierKind.Serial);

        Assert.Equal("Example Game (USA)", index.Entries["SLUS-12345"].Title);
    }

    [Fact]
    public void Parser_IndexesNestedRomSha1ForCartridgeCatalogs()
    {
        using var reader = new StringReader(
            """
            game (
                name "Ristar (USA, Europe)"
                region "USA, Europe"
                rom (
                    name "Ristar (USA, Europe).md"
                    sha1 471EE01E97220D35105CC5E9FB2F03765623CD05
                )
            )
            """);

        var index = LibretroDatCatalog.Parse(reader, GameIdentifierKind.Sha1);

        var entry = Assert.Single(index.Entries).Value;
        Assert.Equal("Ristar (USA, Europe)", entry.Title);
        Assert.Equal("USA, Europe", entry.Region);
        Assert.True(index.Entries.ContainsKey("471EE01E97220D35105CC5E9FB2F03765623CD05"));
    }
}
