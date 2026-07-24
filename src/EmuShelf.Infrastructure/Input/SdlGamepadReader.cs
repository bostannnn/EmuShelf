using System.Reflection;
using System.Runtime.InteropServices;
using EmuShelf.Core.Input;

namespace EmuShelf.Infrastructure.Input;

/// <summary>
/// Reads a controller through SDL2's GameController API, which normalizes any recognized pad to the
/// standard Xbox-style layout and honors the built-in controller database. This gives EmuShelf real
/// physical-controller input independent of Steam Input's keyboard mapping.
///
/// The native library is loaded lazily and every native call is guarded: if SDL2 is not present
/// (e.g. a plain macOS/Windows dev box without it) or initialization fails, the reader reports
/// <see cref="IsAvailable"/> false and returns <see cref="GamepadReading.Disconnected"/> forever, so
/// the app still runs and falls back to keyboard/Steam Input. On SteamOS/Steam Deck libSDL2 is always
/// present, which is where native control matters most.
/// </summary>
public sealed class SdlGamepadReader : IGamepadReader, IDisposable
{
    private const string LibraryName = "SDL2";
    private const uint SdlInitGameController = 0x00002000;

    // SDL_GameControllerButton values (stable across SDL2).
    private const int ButtonA = 0;
    private const int ButtonB = 1;
    private const int ButtonX = 2;
    private const int ButtonY = 3;
    private const int ButtonLeftShoulder = 9;
    private const int ButtonRightShoulder = 10;
    private const int ButtonDpadUp = 11;
    private const int ButtonDpadDown = 12;
    private const int ButtonDpadLeft = 13;
    private const int ButtonDpadRight = 14;

    // SDL_GameControllerAxis values.
    private const int AxisLeftX = 0;
    private const int AxisLeftY = 1;
    private const float AxisRange = 32767f;

    private nint _controller;
    private bool _initialized;
    private bool _initializationFailed;
    private bool _disposed;

    static SdlGamepadReader() =>
        NativeLibrary.SetDllImportResolver(typeof(SdlGamepadReader).Assembly, ResolveLibrary);

    public bool IsAvailable => _initialized && !_initializationFailed;

    public GamepadReading Read()
    {
        if (_disposed || !EnsureInitialized())
            return GamepadReading.Disconnected;

        try
        {
            SDL_GameControllerUpdate();

            if (_controller == 0 || SDL_GameControllerGetAttached(_controller) == 0)
            {
                CloseController();
                TryOpenController();
                if (_controller == 0)
                    return GamepadReading.Disconnected;
            }

            var buttons = GamepadButtons.None;
            buttons |= Held(ButtonA, GamepadButtons.A);
            buttons |= Held(ButtonB, GamepadButtons.B);
            buttons |= Held(ButtonX, GamepadButtons.X);
            buttons |= Held(ButtonY, GamepadButtons.Y);
            buttons |= Held(ButtonLeftShoulder, GamepadButtons.LeftShoulder);
            buttons |= Held(ButtonRightShoulder, GamepadButtons.RightShoulder);
            buttons |= Held(ButtonDpadUp, GamepadButtons.DpadUp);
            buttons |= Held(ButtonDpadDown, GamepadButtons.DpadDown);
            buttons |= Held(ButtonDpadLeft, GamepadButtons.DpadLeft);
            buttons |= Held(ButtonDpadRight, GamepadButtons.DpadRight);

            var leftX = SDL_GameControllerGetAxis(_controller, AxisLeftX) / AxisRange;
            var leftY = SDL_GameControllerGetAxis(_controller, AxisLeftY) / AxisRange;
            return new GamepadReading(buttons, leftX, leftY, true);
        }
        catch (DllNotFoundException)
        {
            _initializationFailed = true;
            return GamepadReading.Disconnected;
        }
        catch (Exception)
        {
            // Never let native-interop trouble take down the UI loop.
            return GamepadReading.Disconnected;
        }
    }

    private GamepadButtons Held(int sdlButton, GamepadButtons flag) =>
        SDL_GameControllerGetButton(_controller, sdlButton) != 0 ? flag : GamepadButtons.None;

    private bool EnsureInitialized()
    {
        if (_initialized)
            return true;
        if (_initializationFailed)
            return false;

        try
        {
            if (SDL_Init(SdlInitGameController) != 0)
            {
                _initializationFailed = true;
                return false;
            }

            _initialized = true;
            TryOpenController();
            return true;
        }
        catch (Exception)
        {
            // DllNotFoundException, EntryPointNotFoundException, or any load failure: disable cleanly.
            _initializationFailed = true;
            return false;
        }
    }

    private void TryOpenController()
    {
        var count = SDL_NumJoysticks();
        for (var index = 0; index < count; index++)
        {
            if (SDL_IsGameController(index) == 0)
                continue;
            var handle = SDL_GameControllerOpen(index);
            if (handle != 0)
            {
                _controller = handle;
                return;
            }
        }
    }

    private void CloseController()
    {
        if (_controller != 0)
        {
            SDL_GameControllerClose(_controller);
            _controller = 0;
        }
    }

    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;

        try
        {
            CloseController();
            if (_initialized)
                SDL_QuitSubSystem(SdlInitGameController);
        }
        catch (Exception)
        {
            // Best-effort teardown; a failed native shutdown must not surface on exit.
        }
    }

    private static nint ResolveLibrary(string libraryName, Assembly assembly, DllImportSearchPath? searchPath)
    {
        if (libraryName != LibraryName)
            return 0;

        // Bundled filenames (from the ppy.SDL2-CS native package) come first so the copy shipped
        // beside the app is preferred; the system soname is a fallback (e.g. SteamOS's libSDL2-2.0.so.0).
        string[] candidates = OperatingSystem.IsWindows()
            ? ["SDL2.dll", "SDL2"]
            : OperatingSystem.IsMacOS()
                ? ["libSDL2.dylib", "libSDL2-2.0.0.dylib", "SDL2"]
                : ["libSDL2.so", "libSDL2-2.0.so.0", "libSDL2.so.0", "SDL2"];

        foreach (var candidate in candidates)
        {
            if (NativeLibrary.TryLoad(candidate, out var handle))
                return handle;
        }

        return 0;
    }

    [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)]
    private static extern int SDL_Init(uint flags);

    [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)]
    private static extern void SDL_QuitSubSystem(uint flags);

    [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)]
    private static extern int SDL_NumJoysticks();

    [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)]
    private static extern int SDL_IsGameController(int joystickIndex);

    [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)]
    private static extern nint SDL_GameControllerOpen(int joystickIndex);

    [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)]
    private static extern void SDL_GameControllerClose(nint gameController);

    [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)]
    private static extern void SDL_GameControllerUpdate();

    [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)]
    private static extern int SDL_GameControllerGetAttached(nint gameController);

    [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)]
    private static extern byte SDL_GameControllerGetButton(nint gameController, int button);

    [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)]
    private static extern short SDL_GameControllerGetAxis(nint gameController, int axis);
}
