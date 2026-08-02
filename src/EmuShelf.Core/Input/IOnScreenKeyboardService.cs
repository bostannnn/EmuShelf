namespace EmuShelf.Core.Input;

/// <summary>
/// Requests a host-owned keyboard after the view has focused a text control. Platforms without a
/// callable keyboard return false and leave ordinary hardware/Steam-shortcut entry available.
/// </summary>
public interface IOnScreenKeyboardService
{
    bool IsSupported { get; }

    bool TryShow(OnScreenKeyboardRequest request);
}

public sealed record OnScreenKeyboardRequest(string Description, bool IsSecret = false);

public sealed class UnsupportedOnScreenKeyboardService : IOnScreenKeyboardService
{
    public static UnsupportedOnScreenKeyboardService Instance { get; } = new();

    private UnsupportedOnScreenKeyboardService()
    {
    }

    public bool IsSupported => false;

    public bool TryShow(OnScreenKeyboardRequest request) => false;
}
