namespace EmuShelf.Core.Settings;

/// <summary>Portable user settings, persisted as JSON in Settings/.</summary>
public sealed record AppSettings
{
    public ThemePreference Theme { get; init; } = ThemePreference.System;

    /// <summary>Persisted library layout. Steam Input maps controller actions to keyboard input.</summary>
    public InterfaceMode InterfaceMode { get; init; } = InterfaceMode.Desktop;

    /// <summary>Network metadata work is disabled until the user explicitly opts in.</summary>
    public bool AutomaticallyFetchMetadataAfterImport { get; init; }

    /// <summary>Prevents the contextual consent prompt from appearing on every import.</summary>
    public bool MetadataConsentPromptShown { get; init; }

    /// <summary>Provider toggles and non-secret scraping preferences.</summary>
    public ScrapingSettings Scraping { get; init; } = new();

    /// <summary>Connected RetroAchievements username, or null when not connected. Not a secret.</summary>
    public string? RetroAchievementsUsername { get; init; }

    /// <summary>RetroAchievements' stable ULID for the connected account. Not a secret.</summary>
    public string? RetroAchievementsUserUlid { get; init; }

    /// <summary>Cloud save-sync configuration. Holds no secret — the OAuth token stays in rclone's own config.</summary>
    public CloudSaveSyncSettings CloudSaveSync { get; init; } = new();

    /// <summary>Installed texture-pack inventory configuration. Read-only discovery only.</summary>
    public TexturePackSettings TexturePacks { get; init; } = new();

    /// <summary>Library presentation — view mode, sort, and what was being shown — restored at launch.</summary>
    public LibraryViewSettings LibraryView { get; init; } = new();

    /// <summary>Main-window size, position, and maximized state, restored at launch.</summary>
    public WindowLayoutSettings WindowLayout { get; init; } = new();
}
