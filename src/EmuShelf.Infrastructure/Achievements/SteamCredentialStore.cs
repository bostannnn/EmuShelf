using EmuShelf.Core.Achievements;

namespace EmuShelf.Infrastructure.Achievements;

/// <summary>Explicit session-only fallback where an OS-backed store is not installed.</summary>
public sealed class SessionSteamCredentialStore : ISteamCredentialStore
{
    private string? _key;
    public bool IsPersistent => false;
    public string? Read() => _key;
    public void Write(string key) => _key = key;
    public void Clear() => _key = null;
}

[System.Runtime.Versioning.SupportedOSPlatform("windows")]
public sealed class WindowsSteamCredentialStore(string path) : ISteamCredentialStore
{
    private readonly WindowsDpapiCredentialStore _store = new(path);
    public bool IsPersistent => true;
    public string? Read() => _store.GetApiKey();
    public void Write(string key) => _store.SaveApiKey(key);
    public void Clear() => _store.ClearApiKey();
}
