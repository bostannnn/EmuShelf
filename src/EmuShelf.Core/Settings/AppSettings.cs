namespace EmuShelf.Core.Settings;

/// <summary>Portable user settings, persisted as JSON in Settings/.</summary>
public sealed record AppSettings
{
    public ThemePreference Theme { get; init; } = ThemePreference.System;
}
