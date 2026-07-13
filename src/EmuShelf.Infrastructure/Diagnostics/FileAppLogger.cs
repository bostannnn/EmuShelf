using System.Globalization;
using System.Text;
using EmuShelf.Core.Diagnostics;
using EmuShelf.Core.Storage;

namespace EmuShelf.Infrastructure.Diagnostics;

/// <summary>
/// Thread-safe, portable daily log files under Logs/. Logging deliberately swallows
/// its own I/O failures so a read-only or disconnected drive cannot crash EmuShelf.
/// </summary>
public sealed class FileAppLogger : IAppLogger
{
    private readonly string _logsDirectory;
    private readonly object _gate = new();

    public FileAppLogger(IAppPaths paths)
    {
        _logsDirectory = paths.LogsDirectory;
    }

    public void Information(string message) => Write("INFO", message, null);

    public void Warning(string message, Exception? exception = null) =>
        Write("WARN", message, exception);

    public void Error(string message, Exception? exception = null) =>
        Write("ERROR", message, exception);

    private void Write(string level, string message, Exception? exception)
    {
        try
        {
            var now = DateTimeOffset.Now;
            var path = Path.Combine(
                _logsDirectory,
                $"EmuShelf-{now:yyyy-MM-dd}.log");
            var entry = new StringBuilder()
                .Append(now.ToString("O", CultureInfo.InvariantCulture))
                .Append(" [")
                .Append(level)
                .Append("] ")
                .AppendLine(message);
            if (exception is not null)
                entry.AppendLine(exception.ToString());

            lock (_gate)
            {
                Directory.CreateDirectory(_logsDirectory);
                File.AppendAllText(path, entry.ToString(), Encoding.UTF8);
            }
        }
        catch
        {
            // Diagnostics must not replace the original operation with a logging failure.
        }
    }
}
