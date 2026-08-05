using System.Diagnostics;

namespace EmuShelf.Infrastructure.Updates;

/// <summary>Small process helpers shared by the platform update appliers.</summary>
internal static class UpdateProcess
{
    /// <summary>Runs a command to completion. Throws when it exits non-zero unless
    /// <paramref name="throwOnError"/> is false (used for best-effort steps like clearing quarantine).</summary>
    public static void Run(string fileName, IReadOnlyList<string> arguments, bool throwOnError = true)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = fileName,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        foreach (var argument in arguments)
            startInfo.ArgumentList.Add(argument);

        using var process = Process.Start(startInfo)
            ?? throw new InvalidOperationException($"Could not start '{fileName}'.");
        process.WaitForExit();
        if (throwOnError && process.ExitCode != 0)
        {
            var error = process.StandardError.ReadToEnd();
            throw new InvalidOperationException(
                $"'{fileName}' exited with code {process.ExitCode}. {error}".Trim());
        }
    }

    /// <summary>Launches a helper detached from this process so it survives our imminent exit.</summary>
    public static void LaunchDetached(string fileName, IReadOnlyList<string> arguments)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = fileName,
            UseShellExecute = false,
            CreateNoWindow = true,
            WindowStyle = ProcessWindowStyle.Hidden,
        };
        foreach (var argument in arguments)
            startInfo.ArgumentList.Add(argument);

        if (Process.Start(startInfo) is null)
            throw new InvalidOperationException($"Could not start the update helper '{fileName}'.");
    }
}
