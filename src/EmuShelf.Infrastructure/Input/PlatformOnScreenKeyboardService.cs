using System.Diagnostics;
using EmuShelf.Core.Input;

namespace EmuShelf.Infrastructure.Input;

/// <summary>
/// Best-effort host keyboard request. Windows exposes an installed touch/accessibility keyboard;
/// SteamOS and macOS keep their normal Steam/hardware-keyboard fallback until a Steamworks-backed
/// implementation is available. No platform-specific type escapes this interface.
/// </summary>
public sealed class PlatformOnScreenKeyboardService : IOnScreenKeyboardService
{
    public bool IsSupported => ResolveWindowsKeyboard() is not null;

    public bool TryShow(OnScreenKeyboardRequest request)
    {
        var keyboard = ResolveWindowsKeyboard();
        if (keyboard is null)
            return false;

        try
        {
            using var process = Process.Start(new ProcessStartInfo
            {
                FileName = keyboard,
                UseShellExecute = true,
            });
            return process is not null;
        }
        catch
        {
            return false;
        }
    }

    private static string? ResolveWindowsKeyboard()
    {
        if (!OperatingSystem.IsWindows())
            return null;

        var candidates = new[]
        {
            Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.CommonProgramFiles),
                "microsoft shared",
                "ink",
                "TabTip.exe"),
            Path.Combine(Environment.SystemDirectory, "osk.exe"),
        };
        return candidates.FirstOrDefault(File.Exists);
    }
}
