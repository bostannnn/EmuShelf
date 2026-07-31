using System.Buffers.Binary;
using System.Text;
using EmuShelf.Core.Library;
using EmuShelf.Integrations.Emulators.Rpcs3;

namespace EmuShelf.Infrastructure.Tests.Emulators;

public sealed class Rpcs3LibrarySourceTests : IDisposable
{
    private readonly string _directory = Path.Combine(
        Path.GetTempPath(),
        "EmuShelfRpcs3Tests",
        Guid.NewGuid().ToString("N"));

    public Rpcs3LibrarySourceTests()
    {
        Directory.CreateDirectory(_directory);
    }

    [Fact]
    public async Task ReadGamesAsync_ReadsOnlyVersionOneListedEntriesAndEnrichesFromTheirParamSfo()
    {
        var configuration = CreateDirectory("config");
        var listedGame = CreateDirectory("games", "Listed Game");
        var unlistedGame = CreateDirectory("games", "Unlisted Game");
        File.WriteAllBytes(
            Path.Combine(listedGame, "PARAM.SFO"),
            CreateParameterSfo("BLES12345", "Listed title"));
        File.WriteAllBytes(
            Path.Combine(unlistedGame, "PARAM.SFO"),
            CreateParameterSfo("BLES54321", "Unlisted title"));
        WriteGameList(configuration, $"BLES12345: '{listedGame}'");

        var source = new Rpcs3LibrarySource(configuration);
        var entry = Assert.Single(await source.ReadGamesAsync());

        Assert.Equal(Rpcs3LibrarySource.SourceId, source.Source.Id);
        Assert.Equal("playstation3", source.Source.SystemId);
        Assert.Equal("BLES12345", entry.SourceEntryId);
        Assert.Equal(listedGame, entry.Path);
        Assert.Equal("Listed title", entry.Title);
        Assert.Equal(GameTitleOrigin.Embedded, entry.TitleOrigin);
        Assert.True(entry.IsAvailable);
    }

    [Fact]
    public async Task ReadGamesAsync_UsesFilenameWhenAListedEntryHasNoReadableParamSfo()
    {
        var configuration = CreateDirectory("config");
        var game = CreateDirectory("games", "Unlabelled Game");
        WriteGameList(configuration, $"BLES12345: '{game}'");

        var entry = Assert.Single(await new Rpcs3LibrarySource(configuration).ReadGamesAsync());

        Assert.Equal("Unlabelled Game", entry.Title);
        Assert.Equal(GameTitleOrigin.Filename, entry.TitleOrigin);
        Assert.True(entry.IsAvailable);
    }

    [Fact]
    public async Task ReadGamesAsync_EnrichesOnlyTheListedDiscLayout()
    {
        var configuration = CreateDirectory("config");
        var discRoot = CreateDirectory("games", "Disc Layout");
        var ps3Game = Path.Combine(discRoot, "PS3_GAME");
        Directory.CreateDirectory(ps3Game);
        File.WriteAllBytes(
            Path.Combine(ps3Game, "PARAM.SFO"),
            CreateParameterSfo("BLUS12345", "Disc title"));
        WriteGameList(configuration, $"BLUS12345: '{discRoot}'");

        var entry = Assert.Single(await new Rpcs3LibrarySource(configuration).ReadGamesAsync());

        Assert.Equal("Disc title", entry.Title);
        Assert.Equal(GameTitleOrigin.Embedded, entry.TitleOrigin);
    }

    [Fact]
    public async Task ReadGamesAsync_RecordsUnavailableForAPathStillListedByRpcs3()
    {
        var configuration = CreateDirectory("config");
        var missingGame = Path.Combine(_directory, "games", "Missing Game");
        WriteGameList(configuration, $"BLES12345: '{missingGame}'");

        var entry = Assert.Single(await new Rpcs3LibrarySource(configuration).ReadGamesAsync());

        Assert.Equal("Missing Game", entry.Title);
        Assert.False(entry.IsAvailable);
    }

    [Fact]
    public async Task ReadGamesAsync_AcceptsRpcs3sBlankGameListAsAnEmptyLibrary()
    {
        var configuration = CreateDirectory("config");
        File.WriteAllText(Path.Combine(configuration, "games.yml"), string.Empty);

        var entries = await new Rpcs3LibrarySource(configuration).ReadGamesAsync();

        Assert.Empty(entries);
    }

    [Fact]
    public async Task ReadGamesAsync_RejectsUnsupportedShapeWithoutChangingRpcs3Data()
    {
        var configuration = CreateDirectory("config");
        var gameList = Path.Combine(configuration, "games.yml");
        File.WriteAllText(gameList, "BLES12345:\n  path: /games/example\n");
        var beforeContents = File.ReadAllText(gameList);
        var beforeModified = File.GetLastWriteTimeUtc(gameList);

        var exception = await Assert.ThrowsAsync<Rpcs3LibraryFormatException>(() =>
            new Rpcs3LibrarySource(configuration).ReadGamesAsync());

        Assert.Contains("No games were imported", exception.Message);
        Assert.Equal(beforeContents, File.ReadAllText(gameList));
        Assert.Equal(beforeModified, File.GetLastWriteTimeUtc(gameList));
    }

    [Fact]
    public async Task ReadGamesAsync_RejectsMissingGameListWithAnActionableMessage()
    {
        var configuration = CreateDirectory("empty-config");

        var exception = await Assert.ThrowsAsync<Rpcs3LibraryFormatException>(() =>
            new Rpcs3LibrarySource(configuration).ReadGamesAsync());

        Assert.Contains("does not contain RPCS3's games.yml", exception.Message);
    }

    [Fact]
    public void LocateConfigurationDirectory_FindsGameListBesideTheConfiguredExecutable()
    {
        var installation = CreateDirectory("Emulators", "RPCS3");
        WriteGameList(installation, "BLES12345: '/games/Listed'");
        var executable = Path.Combine(installation, "rpcs3.exe");
        File.WriteAllText(executable, string.Empty);

        Assert.Equal(installation, Rpcs3LibrarySource.LocateConfigurationDirectory(executable));
    }

    [Fact]
    public void LocateConfigurationDirectory_FindsGameListInTheConfiguredExecutablesConfigDirectory()
    {
        var installation = CreateDirectory("Emulators", "RPCS3");
        var configuration = CreateDirectory("Emulators", "RPCS3", "config");
        WriteGameList(configuration, "BLES12345: '/games/Listed'");
        var executable = Path.Combine(installation, "rpcs3.exe");
        File.WriteAllText(executable, string.Empty);

        Assert.Equal(configuration, Rpcs3LibrarySource.LocateConfigurationDirectory(executable));
    }

    [Fact]
    public void LocateConfigurationDirectory_ReturnsNullWhenNoGameListSitsBesideTheExecutable()
    {
        var installation = CreateDirectory("Emulators", "RPCS3");
        var executable = Path.Combine(installation, "rpcs3.exe");
        File.WriteAllText(executable, string.Empty);

        Assert.Null(Rpcs3LibrarySource.LocateConfigurationDirectory(executable));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void LocateConfigurationDirectory_ReturnsNullForAnUnconfiguredExecutable(string? executablePath)
    {
        Assert.Null(Rpcs3LibrarySource.LocateConfigurationDirectory(executablePath));
    }

    public void Dispose()
    {
        if (Directory.Exists(_directory))
            Directory.Delete(_directory, recursive: true);
    }

    private string CreateDirectory(params string[] components)
    {
        var path = Path.Combine([_directory, .. components]);
        Directory.CreateDirectory(path);
        return path;
    }

    private static void WriteGameList(string configurationDirectory, string contents) =>
        File.WriteAllText(Path.Combine(configurationDirectory, "games.yml"), contents + "\n");

    private static byte[] CreateParameterSfo(string titleId, string title)
    {
        var keys = Encoding.UTF8.GetBytes("TITLE_ID\0TITLE\0");
        var titleIdBytes = Encoding.UTF8.GetBytes(titleId + "\0");
        var titleBytes = Encoding.UTF8.GetBytes(title + "\0");
        const int headerLength = 20;
        const int entryLength = 16;
        var keyTableOffset = headerLength + (2 * entryLength);
        var dataTableOffset = keyTableOffset + keys.Length;
        var bytes = new byte[dataTableOffset + titleIdBytes.Length + titleBytes.Length];

        BinaryPrimitives.WriteUInt32LittleEndian(bytes, 0x46535000);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(8), (uint)keyTableOffset);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(12), (uint)dataTableOffset);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(16), 2);
        WriteEntry(bytes.AsSpan(headerLength, entryLength), 0, titleIdBytes.Length, 0);
        WriteEntry(bytes.AsSpan(headerLength + entryLength, entryLength), 9, titleBytes.Length, titleIdBytes.Length);
        keys.CopyTo(bytes, keyTableOffset);
        titleIdBytes.CopyTo(bytes, dataTableOffset);
        titleBytes.CopyTo(bytes, dataTableOffset + titleIdBytes.Length);
        return bytes;
    }

    private static void WriteEntry(Span<byte> entry, ushort keyOffset, int dataLength, int dataOffset)
    {
        BinaryPrimitives.WriteUInt16LittleEndian(entry, keyOffset);
        BinaryPrimitives.WriteUInt16LittleEndian(entry[2..], 0x0204);
        BinaryPrimitives.WriteUInt32LittleEndian(entry[4..], (uint)dataLength);
        BinaryPrimitives.WriteUInt32LittleEndian(entry[8..], (uint)dataLength);
        BinaryPrimitives.WriteUInt32LittleEndian(entry[12..], (uint)dataOffset);
    }
}
