using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using EmuShelf.Core.SaveSync;
using EmuShelf.Infrastructure.SaveSync;

namespace EmuShelf.Infrastructure.Tests.SaveSync;

public sealed class FileSetSaveSyncTests : TempAppDirectoryTestBase
{
    [Fact]
    public async Task Snapshot_IsStableAcrossProviderEnumerationOrder()
    {
        var root = Path.Combine(BaseDirectory, "card");
        Directory.CreateDirectory(root);
        var first = Path.Combine(root, "a.gci");
        var second = Path.Combine(root, "z.gci");
        await File.WriteAllTextAsync(first, "first");
        await File.WriteAllTextAsync(second, "second");

        var forward = CreateEndpoint(root, [first, second]);
        var reverse = CreateEndpoint(root, [second, first]);

        Assert.Equal(await forward.SnapshotAsync("fileset/game"), await reverse.SnapshotAsync("fileset/game"));
    }

    [Fact]
    public async Task ReadThenWrite_ReplacesOnlyOwnedSiblingsAndPreservesUnrelatedSave()
    {
        var sourceRoot = Path.Combine(BaseDirectory, "source");
        Directory.CreateDirectory(sourceRoot);
        var sourceFirst = Path.Combine(sourceRoot, "one.gci");
        var sourceSecond = Path.Combine(sourceRoot, "two.gci");
        await File.WriteAllTextAsync(sourceFirst, "new one");
        await File.WriteAllTextAsync(sourceSecond, "new two");
        var source = CreateEndpoint(sourceRoot, [sourceFirst, sourceSecond]);
        var sourceSnapshot = await source.SnapshotAsync("fileset/game");
        await using var stream = await source.ReadAsync("fileset/game");
        using var payload = new MemoryStream();
        await stream.CopyToAsync(payload);

        var targetRoot = Path.Combine(BaseDirectory, "target");
        Directory.CreateDirectory(targetRoot);
        var targetFirst = Path.Combine(targetRoot, "one.gci");
        var targetSecond = Path.Combine(targetRoot, "two.gci");
        var unrelated = Path.Combine(targetRoot, "other-game.gci");
        await File.WriteAllTextAsync(targetFirst, "old one");
        await File.WriteAllTextAsync(targetSecond, "old two");
        await File.WriteAllTextAsync(unrelated, "keep me");
        var target = CreateEndpoint(targetRoot, [targetFirst, targetSecond]);

        payload.Position = 0;
        await target.WriteAsync("fileset/game", payload, sourceSnapshot!.ContentHash, DateTimeOffset.UtcNow);

        Assert.Equal("new one", await File.ReadAllTextAsync(targetFirst));
        Assert.Equal("new two", await File.ReadAllTextAsync(targetSecond));
        Assert.Equal("keep me", await File.ReadAllTextAsync(unrelated));
    }

    [Fact]
    public async Task RemoteOnlyWrite_CreatesTheConfiguredSiblingSet()
    {
        var targetRoot = Path.Combine(BaseDirectory, "empty-card");
        var endpoint = CreateEndpoint(targetRoot, []);
        using var payload = CreateArchive(("one.gci", "one"), ("two.gci", "two"));

        await endpoint.WriteAsync(
            "fileset/game",
            payload,
            ExpectedHash(("one.gci", "one"), ("two.gci", "two")),
            DateTimeOffset.UtcNow);

        Assert.Equal("one", await File.ReadAllTextAsync(Path.Combine(targetRoot, "one.gci")));
        Assert.Equal("two", await File.ReadAllTextAsync(Path.Combine(targetRoot, "two.gci")));
    }

    [Fact]
    public async Task WriteRejectsNestedArchiveWithoutTouchingExistingFiles()
    {
        var root = Path.Combine(BaseDirectory, "card");
        Directory.CreateDirectory(root);
        var existing = Path.Combine(root, "save.gci");
        await File.WriteAllTextAsync(existing, "original");
        var endpoint = CreateEndpoint(root, [existing]);
        using var payload = CreateArchive(("nested/save.gci", "incoming"));

        await Assert.ThrowsAsync<InvalidDataException>(
            () => endpoint.WriteAsync(
                "fileset/game",
                payload,
                ExpectedHash(("nested/save.gci", "incoming")),
                DateTimeOffset.UtcNow));

        Assert.Equal("original", await File.ReadAllTextAsync(existing));
    }

    [Fact]
    public async Task WriteRejectsMismatchedSemanticHashWithoutTouchingExistingFiles()
    {
        var root = Path.Combine(BaseDirectory, "card");
        Directory.CreateDirectory(root);
        var existing = Path.Combine(root, "save.gci");
        await File.WriteAllTextAsync(existing, "original");
        var endpoint = CreateEndpoint(root, [existing]);
        using var payload = CreateArchive(("save.gci", "incoming"));

        await Assert.ThrowsAsync<InvalidDataException>(() => endpoint.WriteAsync(
            "fileset/game",
            payload,
            ExpectedHash(("save.gci", "different")),
            DateTimeOffset.UtcNow));

        Assert.Equal("original", await File.ReadAllTextAsync(existing));
    }

    [Fact]
    public async Task WriteRejectsCollisionWithAnotherUnitWithoutTouchingEitherSave()
    {
        var root = Path.Combine(BaseDirectory, "card");
        Directory.CreateDirectory(root);
        var owned = Path.Combine(root, "owned.gci");
        var other = Path.Combine(root, "other.gci");
        await File.WriteAllTextAsync(owned, "owned original");
        await File.WriteAllTextAsync(other, "other original");
        var endpoint = CreateEndpoint(root, [owned]);
        using var payload = CreateArchive(("owned.gci", "owned incoming"), ("other.gci", "collision"));

        await Assert.ThrowsAsync<InvalidDataException>(
            () => endpoint.WriteAsync(
                "fileset/game",
                payload,
                ExpectedHash(("owned.gci", "owned incoming"), ("other.gci", "collision")),
                DateTimeOffset.UtcNow));

        Assert.Equal("owned original", await File.ReadAllTextAsync(owned));
        Assert.Equal("other original", await File.ReadAllTextAsync(other));
    }

    [Fact]
    public async Task PartialInstallFailure_RestoresEveryDisplacedMember()
    {
        var root = Path.Combine(BaseDirectory, "card");
        Directory.CreateDirectory(root);
        var owned = Path.Combine(root, "one.gci");
        await File.WriteAllTextAsync(owned, "original");
        Directory.CreateDirectory(Path.Combine(root, "two.gci"));
        var endpoint = CreateEndpoint(root, [owned]);
        using var payload = CreateArchive(("one.gci", "incoming one"), ("two.gci", "incoming two"));

        await Assert.ThrowsAsync<IOException>(
            () => endpoint.WriteAsync(
                "fileset/game",
                payload,
                ExpectedHash(("one.gci", "incoming one"), ("two.gci", "incoming two")),
                DateTimeOffset.UtcNow));

        Assert.Equal("original", await File.ReadAllTextAsync(owned));
        Assert.True(Directory.Exists(Path.Combine(root, "two.gci")));
    }

    private FileSystemLocalSaveEndpoint CreateEndpoint(string root, IReadOnlyList<string> members) =>
        new(new FileSetProvider(root, members), AppPaths);

    private static MemoryStream CreateArchive(params (string Name, string Content)[] files)
    {
        var stream = new MemoryStream();
        using (var archive = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true))
        {
            foreach (var (name, content) in files)
            {
                var entry = archive.CreateEntry(name);
                using var writer = new StreamWriter(entry.Open());
                writer.Write(content);
            }
        }
        stream.Position = 0;
        return stream;
    }

    private static string ExpectedHash(params (string Name, string Content)[] files)
    {
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        foreach (var (name, content) in files.OrderBy(file => file.Name, StringComparer.Ordinal))
        {
            var path = Encoding.UTF8.GetBytes(name);
            hash.AppendData(BitConverter.GetBytes(path.Length));
            hash.AppendData(path);
            hash.AppendData(Encoding.UTF8.GetBytes(content));
        }
        return Convert.ToHexString(hash.GetHashAndReset());
    }

    private sealed class FileSetProvider(string root, IReadOnlyList<string> members) : ISaveLocationProvider
    {
        public string SystemId => "fileset";
        public string UnitIdPrefix => "fileset/";

        public Task<IReadOnlyList<SaveUnit>> GetSaveUnitsAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<SaveUnit>>([]);

        public SaveUnitLocation? ResolveUnit(string unitId) =>
            unitId == "fileset/game"
                ? new SaveUnitLocation(root, Path.GetDirectoryName(root)!, SaveUnitKind.FileSet, members)
                : null;

        public bool IsIncomingFileSetMemberAllowed(string unitId, string filePath) =>
            unitId == "fileset/game" && Path.GetExtension(filePath) == ".gci";
    }
}
