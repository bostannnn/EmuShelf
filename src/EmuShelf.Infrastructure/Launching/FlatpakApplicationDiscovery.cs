using System.Diagnostics;

namespace EmuShelf.Infrastructure.Launching;

/// <summary>Lists supported standalone emulator Flatpaks; callers must select a result explicitly.</summary>
public sealed class FlatpakApplicationDiscovery
{
    private static readonly IReadOnlyDictionary<string, string> ApplicationByEmulatorId =
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["pcsx2"] = "net.pcsx2.PCSX2",
            ["dolphin"] = "org.DolphinEmu.dolphin-emu",
            ["rpcs3"] = "net.rpcs3.RPCS3",
            ["ppsspp"] = "org.ppsspp.PPSSPP",
        };

    public static readonly IReadOnlyList<string> SupportedApplicationIds =
    [
        "net.pcsx2.PCSX2",
        "org.DolphinEmu.dolphin-emu",
        "net.rpcs3.RPCS3",
        "org.ppsspp.PPSSPP",
    ];

    public IReadOnlyList<string> FindInstalled()
    {
        try
        {
            using var process = Process.Start(new ProcessStartInfo
            {
                FileName = "flatpak",
                Arguments = "list --app --columns=application",
                UseShellExecute = false,
                RedirectStandardOutput = true,
            });
            if (process is null)
                return [];
            var output = process.StandardOutput.ReadToEnd();
            process.WaitForExit();
            if (process.ExitCode != 0)
                return [];

            var installed = output.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries)
                .Select(value => value.Trim())
                .ToHashSet(StringComparer.Ordinal);
            return SupportedApplicationIds.Where(installed.Contains).ToArray();
        }
        catch (Exception exception) when (exception is InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            return [];
        }
    }

    public IReadOnlyList<string> FindInstalledForEmulator(string emulatorId)
    {
        var installed = FindInstalled();
        return ApplicationByEmulatorId.TryGetValue(emulatorId, out var applicationId) &&
               installed.Contains(applicationId, StringComparer.Ordinal)
            ? [applicationId]
            : [];
    }
}
