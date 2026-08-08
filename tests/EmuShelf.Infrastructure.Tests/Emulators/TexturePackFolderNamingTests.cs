using EmuShelf.Core.Metadata;
using EmuShelf.Core.TexturePacks;

namespace EmuShelf.Infrastructure.Tests.Emulators;

public sealed class TexturePackFolderNamingTests
{
    [Fact]
    public void Serial_KeepsTheSerialVerbatimUpperCased()
    {
        var name = TexturePackFolderNaming.Build(
            TexturePackFolderKind.Serial,
            [new GameIdentifier(GameIdentifierKind.Serial, "slus-00594", "test")]);

        Assert.Equal("SLUS-00594", name);
    }

    [Fact]
    public void PspGameId_StripsSeparatorsSoTheFolderMatchesWhatAScanReadsBack()
    {
        var name = TexturePackFolderNaming.Build(
            TexturePackFolderKind.PspGameId,
            [new GameIdentifier(GameIdentifierKind.Serial, "ULES-00841", "test")]);

        Assert.Equal("ULES00841", name);
    }

    [Fact]
    public void DolphinDiscId_UsesTheDiscIdUpperCased()
    {
        var name = TexturePackFolderNaming.Build(
            TexturePackFolderKind.DolphinDiscId,
            [new GameIdentifier(GameIdentifierKind.DiscId, "gale01", "test")]);

        Assert.Equal("GALE01", name);
    }

    [Fact]
    public void Nintendo3dsTitleId_UsesTheTitleIdUpperCased()
    {
        var name = TexturePackFolderNaming.Build(
            TexturePackFolderKind.Nintendo3dsTitleId,
            [new GameIdentifier(GameIdentifierKind.TitleId, "00040000001cb200", "test")]);

        Assert.Equal("00040000001CB200", name);
    }

    [Fact]
    public void PrefersThePrimaryIdentifierWhenSeveralOfTheKindExist()
    {
        var name = TexturePackFolderNaming.Build(
            TexturePackFolderKind.Serial,
            [
                new GameIdentifier(GameIdentifierKind.Serial, "SLUS-00001", "disc-2"),
                new GameIdentifier(GameIdentifierKind.Serial, "SLUS-00002", "disc-1", IsPrimary: true),
            ]);

        Assert.Equal("SLUS-00002", name);
    }

    [Fact]
    public void ReturnsNull_WhenTheGameHasNoIdentifierOfTheRequiredKind()
    {
        // A DiscId is present but the emulator keys folders by serial: no serial → nothing to build.
        var name = TexturePackFolderNaming.Build(
            TexturePackFolderKind.Serial,
            [new GameIdentifier(GameIdentifierKind.DiscId, "GALE01", "test")]);

        Assert.Null(name);
    }

    [Fact]
    public void ReturnsNull_ForAnEmptyIdentifierList()
    {
        Assert.Null(TexturePackFolderNaming.Build(TexturePackFolderKind.Serial, []));
    }

    [Theory]
    [InlineData("SLUS/00594")]
    [InlineData("..")]
    [InlineData("evil\\pack")]
    [InlineData("C:something")]
    public void ReturnsNull_WhenAnIdentifierWouldNotBeASingleFolderSegment(string value)
    {
        // Defense in depth: a malformed identifier must never escape the texture root via Path.Combine.
        var name = TexturePackFolderNaming.Build(
            TexturePackFolderKind.Serial,
            [new GameIdentifier(GameIdentifierKind.Serial, value, "test")]);

        Assert.Null(name);
    }
}
