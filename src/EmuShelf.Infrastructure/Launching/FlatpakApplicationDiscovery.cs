using System.Diagnostics;

namespace EmuShelf.Infrastructure.Launching;

/// <summary>Lists supported standalone emulator Flatpaks; callers must select a result explicitly.</summary>
public sealed class FlatpakApplicationDiscovery
{
    private static readonly IReadOnlyDictionary<string, string> ApplicationByEmulatorId =
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["retroarch"] = "org.libretro.RetroArch",
            ["pcsx2"] = "net.pcsx2.PCSX2",
            ["dolphin"] = "org.DolphinEmu.dolphin-emu",
            ["rpcs3"] = "net.rpcs3.RPCS3",
            ["ppsspp"] = "org.ppsspp.PPSSPP",
            // Flathub publishes melonDS release builds only; the nightly channel is installed by hand,
            // so it maps to no Flatpak and its picker offers a direct executable alone.
            ["melonds"] = "net.kuribo64.melonDS",
        };

    public static readonly IReadOnlyList<string> SupportedApplicationIds =
    [
        "org.libretro.RetroArch",
        "net.pcsx2.PCSX2",
        "org.DolphinEmu.dolphin-emu",
        "net.rpcs3.RPCS3",
        "org.ppsspp.PPSSPP",
        "net.kuribo64.melonDS",
    ];

    /// <summary>One installed Flatpak ref: its application id and the branch it is installed on.</summary>
    public readonly record struct InstalledRef(string AppId, string Branch);

    /// <summary>Every installed branch of the supported emulator Flatpaks.</summary>
    public IReadOnlyList<InstalledRef> FindInstalledRefs()
    {
        var output = RunList();
        if (output is null)
            return [];

        var supported = SupportedApplicationIds.ToHashSet(StringComparer.Ordinal);
        return ParseInstalledRefs(output)
            .Where(reference => supported.Contains(reference.AppId))
            .ToArray();
    }

    /// <summary>
    /// The Flatpak references to offer for an emulator, one per installed branch. When exactly one
    /// branch is installed the bare application id is returned (unambiguous on its own); when several
    /// branches are installed each is returned branch-qualified (e.g. <c>net.pcsx2.PCSX2//beta</c>) so
    /// the user can pin the stable or the nightly build explicitly.
    /// </summary>
    public IReadOnlyList<string> FindInstalledForEmulator(string emulatorId)
    {
        if (!ApplicationByEmulatorId.TryGetValue(emulatorId, out var applicationId))
            return [];

        var branches = FindInstalledRefs()
            .Where(reference => string.Equals(reference.AppId, applicationId, StringComparison.Ordinal))
            .Select(reference => reference.Branch)
            .Distinct(StringComparer.Ordinal)
            .OrderBy(branch => branch, StringComparer.Ordinal)
            .ToArray();

        return branches switch
        {
            { Length: 0 } => [],
            { Length: 1 } => [applicationId],
            _ => branches.Select(branch => $"{applicationId}//{branch}").ToArray(),
        };
    }

    // `flatpak list --columns=application,branch` prints one tab-separated row per installed ref and
    // no header. A row may carry an empty branch column on old flatpak builds; such rows are dropped
    // so a branchless ref never masquerades as an installed branch.
    internal static IEnumerable<InstalledRef> ParseInstalledRefs(string listOutput)
    {
        foreach (var line in listOutput.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries))
        {
            var columns = line.Split('\t');
            if (columns.Length < 2)
                continue;
            var appId = columns[0].Trim();
            var branch = columns[1].Trim();
            if (appId.Length == 0 || branch.Length == 0)
                continue;
            yield return new InstalledRef(appId, branch);
        }
    }

    private static string? RunList()
    {
        try
        {
            using var process = Process.Start(new ProcessStartInfo
            {
                FileName = "flatpak",
                UseShellExecute = false,
                RedirectStandardOutput = true,
                ArgumentList = { "list", "--app", "--columns=application,branch" },
            });
            if (process is null)
                return null;
            var output = process.StandardOutput.ReadToEnd();
            process.WaitForExit();
            return process.ExitCode == 0 ? output : null;
        }
        catch (Exception exception) when (exception is InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            return null;
        }
    }
}
