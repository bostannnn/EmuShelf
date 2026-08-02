using System.Diagnostics;
using EmuShelf.Core.SaveSync;
using EmuShelf.Infrastructure.SaveSync;
using EmuShelf.Infrastructure.Storage;
using EmuShelf.Integrations.Emulators.RetroArch;
using EmuShelf.Integrations.Emulators.Rpcs3;

namespace EmuShelf.Infrastructure.Tests.SaveSync;

public sealed class TempPerfProbe
{
    [Fact]
    public async Task Probe()
    {
        if (Environment.GetEnvironmentVariable("EMUSHELF_TEMP_PERF") != "1")
            return;

        var paths = new AppPaths(Path.Combine(Path.GetTempPath(), "emushelf-perf-" + Guid.NewGuid().ToString("N")));
        paths.EnsureDirectoriesExist();
        var lines = new List<string>();

        var rpcs3 = new Rpcs3SaveLocationProvider(@"F:\ES-DE\Emulators\rpcs3");
        await MeasureAsync("playstation3", rpcs3, paths, lines);

        var names = Directory.EnumerateFiles(@"F:\ES-DE\ROMs\nds")
            .Select(Path.GetFileNameWithoutExtension)
            .ToArray();
        var nds = new RetroArchSaveLocationProvider(
            "nds",
            @"F:\ES-DE\Emulators\RetroArch\cores\melondsds_libretro.dll",
            @"F:\ES-DE\Emulators\RetroArch",
            gameFileNames: () => names!);
        await MeasureAsync("nds", nds, paths, lines);

        await File.WriteAllLinesAsync(Environment.GetEnvironmentVariable("EMUSHELF_TEMP_PERF_OUT")!, lines);
        try { Directory.Delete(paths.BaseDirectory, true); } catch (IOException) { }
    }

    private static async Task MeasureAsync(
        string label,
        ISaveLocationProvider provider,
        AppPaths paths,
        List<string> lines)
    {
        var endpoint = new FileSystemLocalSaveEndpoint(provider, paths);
        var enumerate = Stopwatch.StartNew();
        var units = await provider.GetSaveUnitsAsync();
        enumerate.Stop();

        var snapshot = Stopwatch.StartNew();
        long bytes = 0;
        var files = 0;
        foreach (var unit in units)
        {
            await endpoint.SnapshotAsync(unit.UnitId);
            var location = provider.ResolveUnit(unit.UnitId)!;
            if (location.Kind == SaveUnitKind.Folder && Directory.Exists(location.Path))
            {
                foreach (var file in Directory.EnumerateFiles(location.Path, "*", SearchOption.AllDirectories))
                {
                    bytes += new FileInfo(file).Length;
                    files++;
                }
            }
            else if (File.Exists(location.Path))
            {
                bytes += new FileInfo(location.Path).Length;
                files++;
            }
        }

        snapshot.Stop();
        lines.Add($"{label,-14} units={units.Count,3} files={files,5} MB={bytes / 1024.0 / 1024.0,8:0.0} " +
                  $"enumerate={enumerate.ElapsedMilliseconds,6}ms hashAll={snapshot.ElapsedMilliseconds,6}ms");

        // Second pass: how much of that is cold-cache disk read versus hashing.
        var again = Stopwatch.StartNew();
        foreach (var unit in units)
            await endpoint.SnapshotAsync(unit.UnitId);
        again.Stop();
        lines.Add($"{label,-14} hashAll (warm)={again.ElapsedMilliseconds}ms");
    }
}
