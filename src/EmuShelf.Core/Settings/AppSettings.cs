namespace EmuShelf.Core.Settings;

/// <summary>Portable user settings, persisted as JSON in Settings/.</summary>
public sealed record AppSettings
{
    public ThemePreference Theme { get; init; } = ThemePreference.System;

    /// <summary>Network metadata work is disabled until the user explicitly opts in.</summary>
    public bool AutomaticallyFetchMetadataAfterImport { get; init; }

    /// <summary>Prevents the contextual consent prompt from appearing on every import.</summary>
    public bool MetadataConsentPromptShown { get; init; }
}
