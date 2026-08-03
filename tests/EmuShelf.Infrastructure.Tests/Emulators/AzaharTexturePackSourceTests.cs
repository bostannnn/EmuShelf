using EmuShelf.Core.TexturePacks;
using EmuShelf.Integrations.Emulators.Azahar;

namespace EmuShelf.Infrastructure.Tests.Emulators;

public sealed class AzaharTexturePackSourceTests : IDisposable
{
    private readonly string _root = Directory.CreateTempSubdirectory("emushelf-azahar-textures").FullName;

    public void Dispose()
    {
        try
        {
            Directory.Delete(_root, recursive: true);
        }
        catch (IOException)
        {
        }
    }

    [Fact]
    public async Task Source_ClassifiesTitleIdFoldersByReplacementContent()
    {
        var textures = Path.Combine(_root, "load", "textures");
        CreateFile(Path.Combine(textures, "0004000000033500", "new", "tex_001.png")); // usable
        Directory.CreateDirectory(Path.Combine(textures, "0004000000033501"));         // empty → dumps/empty
        CreateFile(Path.Combine(textures, "notatitle", "readme.txt"));                 // wrong name

        var snapshot = await new AzaharTexturePackSource("i", textures).ScanAsync();

        Assert.Equal(TexturePackRootStatus.Ready, snapshot.RootStatus);

        var usable = snapshot.Entries.Single(entry => entry.PackKey == "0004000000033500");
        Assert.Equal(TexturePackContentStatus.Usable, usable.ContentStatus);
        var key = Assert.Single(usable.MatchKeys);
        Assert.Equal(TexturePackMatchRule.Nintendo3dsTitleId, key.Rule);
        Assert.Equal("0004000000033500", key.Value);

        Assert.Equal(
            TexturePackContentStatus.EmptyOrDumpsOnly,
            snapshot.Entries.Single(entry => entry.PackKey == "0004000000033501").ContentStatus);
        var unrecognized = snapshot.Entries.Single(entry => entry.PackKey == "notatitle");
        Assert.Equal(TexturePackContentStatus.UnrecognizedLayout, unrecognized.ContentStatus);
        Assert.Empty(unrecognized.MatchKeys);
    }

    [Fact]
    public async Task RootResolver_AppendsLoadTextures_AndHonorsOverride()
    {
        var userDirectory = Path.Combine(_root, "user");

        var resolved = await new AzaharTextureRootResolver("i", userDirectory).ResolveAsync();
        Assert.Equal(Path.Combine(userDirectory, "load", "textures"), resolved.RootDirectory);

        var overrideDirectory = Path.Combine(_root, "chosen");
        var overridden = await new AzaharTextureRootResolver("i", userDirectory, overrideDirectory).ResolveAsync();
        Assert.Equal(overrideDirectory, overridden.RootDirectory);
    }

    private static void CreateFile(string path)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, "x");
    }
}
