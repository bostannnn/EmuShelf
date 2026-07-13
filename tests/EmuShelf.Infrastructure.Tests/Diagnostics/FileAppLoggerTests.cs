using EmuShelf.Infrastructure.Diagnostics;

namespace EmuShelf.Infrastructure.Tests.Diagnostics;

public class FileAppLoggerTests : TempAppDirectoryTestBase
{
    [Fact]
    public void WritesPortableDailyLogWithExceptionDetails()
    {
        AppPaths.EnsureDirectoriesExist();
        var logger = new FileAppLogger(AppPaths);

        logger.Information("Application started");
        logger.Error("Import failed", new IOException("drive disconnected"));

        var logPath = Assert.Single(Directory.EnumerateFiles(AppPaths.LogsDirectory));
        var log = File.ReadAllText(logPath);
        Assert.Contains("[INFO] Application started", log);
        Assert.Contains("[ERROR] Import failed", log);
        Assert.Contains("System.IO.IOException: drive disconnected", log);
    }

    [Fact]
    public void LoggingFailureNeverEscapesToTheApplication()
    {
        Directory.CreateDirectory(BaseDirectory);
        File.WriteAllText(AppPaths.LogsDirectory, "not a directory");
        var logger = new FileAppLogger(AppPaths);

        var exception = Record.Exception(() => logger.Error("Cannot write this"));

        Assert.Null(exception);
    }
}
