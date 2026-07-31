namespace EmuShelf.Core.SaveSync;

/// <summary>
/// Raised when an emulator's own configuration cannot be read or has an unsupported shape, so the
/// provider refuses to guess at a save location.
///
/// Every provider-specific configuration exception derives from this type so callers catch one
/// base rather than enumerating each emulator. Adding a provider whose exception is missing from a
/// hand-written catch filter would otherwise let it escape and fault the whole sync.
/// </summary>
public abstract class SaveProviderConfigurationException : Exception
{
    protected SaveProviderConfigurationException(string message)
        : base(message)
    {
    }

    protected SaveProviderConfigurationException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
