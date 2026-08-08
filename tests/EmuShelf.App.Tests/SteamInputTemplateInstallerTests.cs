using Avalonia.Headless.XUnit;
using EmuShelf.App.Services;

namespace EmuShelf.App.Tests;

public sealed class SteamInputTemplateInstallerTests : IDisposable
{
    private readonly string _root = Directory.CreateTempSubdirectory("emushelf-steam").FullName;

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
    public void Install_CopiesTemplateIntoSteamControllerTemplates()
    {
        var steamRoot = Path.Combine(_root, "Steam");
        Directory.CreateDirectory(Path.Combine(steamRoot, "controller_base", "templates"));
        var installer = new SteamInputTemplateInstaller(
            resolveSteamRoot: () => steamRoot,
            openBundledTemplate: () => new MemoryStream("template-bytes"u8.ToArray()));

        var result = installer.Install();

        Assert.Equal(SteamTemplateInstallStatus.Installed, result.Status);
        var expected = Path.Combine(steamRoot, "controller_base", "templates", "EmuShelf.vdf");
        Assert.Equal(expected, result.Detail);
        Assert.Equal("template-bytes", File.ReadAllText(expected));
    }

    [Fact]
    public void Install_CreatesTheTemplatesFolderWhenAbsent()
    {
        var steamRoot = Path.Combine(_root, "Steam"); // controller_base/templates does not exist yet
        var installer = new SteamInputTemplateInstaller(
            resolveSteamRoot: () => steamRoot,
            openBundledTemplate: () => new MemoryStream("x"u8.ToArray()));

        var result = installer.Install();

        Assert.Equal(SteamTemplateInstallStatus.Installed, result.Status);
        Assert.True(File.Exists(Path.Combine(steamRoot, "controller_base", "templates", "EmuShelf.vdf")));
    }

    [Fact]
    public void Install_WhenSteamNotFound_ReportsSo()
    {
        var installer = new SteamInputTemplateInstaller(
            resolveSteamRoot: () => null,
            openBundledTemplate: () => new MemoryStream());

        var result = installer.Install();

        Assert.Equal(SteamTemplateInstallStatus.SteamNotFound, result.Status);
    }

    [Fact]
    public void Install_ReRun_OverwritesTheExistingTemplate()
    {
        var steamRoot = Path.Combine(_root, "Steam");
        Directory.CreateDirectory(Path.Combine(steamRoot, "controller_base", "templates"));
        var installer = new SteamInputTemplateInstaller(
            resolveSteamRoot: () => steamRoot,
            openBundledTemplate: () => new MemoryStream("v2"u8.ToArray()));

        installer.Install();
        var result = installer.Install();

        Assert.Equal(SteamTemplateInstallStatus.Installed, result.Status);
        Assert.Equal("v2", File.ReadAllText(Path.Combine(steamRoot, "controller_base", "templates", "EmuShelf.vdf")));
    }

    // Uses the real bundled avares resource (only the Steam dir is faked), so a wrong URI or a missing
    // asset fails here instead of silently at runtime.
    [AvaloniaFact]
    public void Install_WithBundledTemplate_WritesTheRealEmuShelfLayout()
    {
        var steamRoot = Path.Combine(_root, "Steam");
        var installer = new SteamInputTemplateInstaller(resolveSteamRoot: () => steamRoot);

        var result = installer.Install();

        Assert.Equal(SteamTemplateInstallStatus.Installed, result.Status);
        var text = File.ReadAllText(Path.Combine(steamRoot, "controller_base", "templates", "EmuShelf.vdf"));
        Assert.Contains("EmuShelf", text);
        Assert.Contains("key_press F8", text);
    }
}
