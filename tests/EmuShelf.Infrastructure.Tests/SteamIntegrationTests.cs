using EmuShelf.Core.Launching.Android;
using EmuShelf.Core.Metadata;
using EmuShelf.Integrations.Importing;
using EmuShelf.Integrations.Emulators.Android;
using EmuShelf.Integrations.Metadata;
using EmuShelf.Integrations.Systems;

namespace EmuShelf.Infrastructure.Tests;

public class SteamIntegrationTests : TempAppDirectoryTestBase
{
    private string Shortcut(string content, string extension = ".steam")
    {
        Directory.CreateDirectory(BaseDirectory);
        var path = Path.Combine(BaseDirectory, "80 Days" + extension);
        File.WriteAllText(path, content);
        return path;
    }

    [Fact]
    public void Export_ImportsAsSteam_AndLaunchesAnIntegerAppId()
    {
        var path = Shortcut("381780\n");
        var rules = new FileImportRules();
        var steam = KnownSystems.All.Single(s => s.Id == "steam");
        Assert.True(rules.IsFolderCandidate(path, steam));
        Assert.False(rules.IsFolderCandidate(path, KnownSystems.All.Single(s => s.Id == "arcade")));
        var ids = rules.ReadImportMetadata(path, steam).Identifiers;
        Assert.Contains(ids, i => i.Kind == GameIdentifierKind.SteamAppId && i.Value == "381780");
        var launch = AndroidLaunchResolver.Resolve("steam", path);
        Assert.True(launch.Success);
        Assert.Equal("app.gamenative/app.gamenative.MainActivity", launch.Intent!.Component);
        Assert.Equal("app.gamenative.LAUNCH_GAME", launch.Intent.Action);
        Assert.Equal(381780, launch.Intent.IntExtras!["app_id"]);
        Assert.Equal("STEAM", launch.Intent.StringExtras["game_source"]);
        Assert.False(launch.Intent.StringExtras.ContainsKey("app_id"));
        Assert.Null(launch.Intent.DataUri);
        Assert.False(launch.Intent.ClearTask);
        Assert.Equal("381780\n", File.ReadAllText(path));
        var cover = Assert.Single(new SteamArtworkProvider().GetCandidates(ids, null));
        Assert.EndsWith("/381780/library_600x900.jpg", cover.SourceUri.AbsoluteUri);
    }

    [Theory]
    [InlineData("")]
    [InlineData("0")]
    [InlineData("-1")]
    [InlineData("2147483648")]
    [InlineData("381780; rm -rf games")]
    [InlineData("381780\n2161700")]
    [InlineData("app.gamenative.LAUNCH_GAME")]
    public void InvalidExports_AreNeitherImportedNorLaunched(string value)
    {
        var path = Shortcut(value);
        Assert.Null(SteamShortcutReader.TryRead(path));
        Assert.False(new FileImportRules().IsFolderCandidate(path, KnownSystems.All.Single(s => s.Id == "steam")));
        Assert.False(AndroidLaunchResolver.Resolve("steam", path).Success);
    }

    [Fact]
    public void MissingOversizedAndOtherStoreExports_AreRejected()
    {
        Assert.Null(SteamShortcutReader.TryRead(Shortcut(new string('1', 65))));
        Assert.Null(SteamShortcutReader.TryRead(Shortcut("381780", ".pcgame")));
        var path = Shortcut("381780");
        File.Delete(path);
        Assert.False(AndroidLaunchResolver.Resolve("steam", path).Success);
    }
}
