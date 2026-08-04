namespace EmuShelf.Infrastructure.Storage;

/// <summary>
/// A portable at-rest text blob for a single credential value. Implementations differ only in how the
/// bytes are protected — DPAPI on Windows, an application-embedded AES-GCM wrap elsewhere — so a
/// credential store can serialize its payload once and stay platform-agnostic.
/// </summary>
internal interface IProtectedTextStore
{
    /// <summary>The stored value, or <see langword="null"/> when nothing is stored or it is unreadable.</summary>
    string? Read();

    /// <summary>Writes <paramref name="value"/>, replacing any existing blob.</summary>
    void Write(string value);

    /// <summary>Removes the blob if it exists.</summary>
    void Clear();
}
