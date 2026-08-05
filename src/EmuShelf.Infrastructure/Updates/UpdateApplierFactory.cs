using EmuShelf.Core.Diagnostics;
using EmuShelf.Core.Storage;
using EmuShelf.Core.Updates;

namespace EmuShelf.Infrastructure.Updates;

/// <summary>Selects the update applier for the running platform.</summary>
public static class UpdateApplierFactory
{
    public static IUpdateApplier Create(IAppPaths paths, IAppLogger logger)
    {
        if (OperatingSystem.IsWindows())
            return new WindowsUpdateApplier(paths, logger);
        if (OperatingSystem.IsMacOS())
            return new MacUpdateApplier(logger);
        if (OperatingSystem.IsLinux())
            return new LinuxAppImageUpdateApplier(logger);
        return new UnsupportedUpdateApplier();
    }
}
