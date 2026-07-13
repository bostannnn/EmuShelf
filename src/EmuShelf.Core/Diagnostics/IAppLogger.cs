namespace EmuShelf.Core.Diagnostics;

/// <summary>
/// Minimal application logging boundary. Implementations must never throw back into
/// user-facing operations; logging is diagnostic and cannot become a new failure mode.
/// </summary>
public interface IAppLogger
{
    void Information(string message);
    void Warning(string message, Exception? exception = null);
    void Error(string message, Exception? exception = null);
}

public sealed class NullAppLogger : IAppLogger
{
    public static NullAppLogger Instance { get; } = new();

    private NullAppLogger()
    {
    }

    public void Information(string message)
    {
    }

    public void Warning(string message, Exception? exception = null)
    {
    }

    public void Error(string message, Exception? exception = null)
    {
    }
}
