using EmuShelf.Core.Launching;

namespace EmuShelf.Infrastructure.Tests.Launching;

public class ArgumentTemplateTests : IDisposable
{
    private readonly string _directory = Path.Combine(
        Path.GetTempPath(),
        "EmuShelfArgumentTests",
        Guid.NewGuid().ToString("N"));

    public ArgumentTemplateTests()
    {
        Directory.CreateDirectory(_directory);
    }

    [Fact]
    public void Expand_ProducesDistinctArgumentsAndAllDocumentedPlaceholders()
    {
        var gameDirectory = Path.Combine(_directory, "Game Folder");
        var gamePath = Path.Combine(gameDirectory, "Disc One.cue");
        var emulatorDirectory = Path.Combine(_directory, "Emulator Folder");
        var emulatorPath = Path.Combine(emulatorDirectory, "emulator.exe");

        var arguments = ArgumentTemplate.Expand(
            "-batch -- \"{GamePath}\" --dir=\"{GameDirectory}\" " +
            "--name=\"{GameFileName}\" --emu=\"{EmulatorDirectory}\"",
            gamePath,
            emulatorPath);

        Assert.Equal(
        [
            "-batch",
            "--",
            gamePath,
            $"--dir={gameDirectory}",
            "--name=Disc One.cue",
            $"--emu={emulatorDirectory}",
        ],
        arguments);
    }

    [Fact]
    public void Expand_DoesNotInterpretShellMetacharacters()
    {
        var arguments = ArgumentTemplate.Expand(
            "\"{GamePath}\" \"; rm -rf something\"",
            Path.Combine(_directory, "$(touch nope).cue"),
            Path.Combine(_directory, "emu.exe"));

        Assert.Equal(
        [
            Path.Combine(_directory, "$(touch nope).cue"),
            "; rm -rf something",
        ],
        arguments);
    }

    [Theory]
    [InlineData("\"{GamePath}")]
    [InlineData("{Unknown}")]
    [InlineData("{GamePath")]
    [InlineData("GamePath}")]
    public void Expand_RejectsMalformedTemplates(string template)
    {
        Assert.Throws<FormatException>(() => ArgumentTemplate.Expand(
            template,
            Path.Combine(_directory, "game.cue"),
            Path.Combine(_directory, "emu.exe")));
    }

    public void Dispose()
    {
        if (Directory.Exists(_directory))
            Directory.Delete(_directory, recursive: true);
    }
}
