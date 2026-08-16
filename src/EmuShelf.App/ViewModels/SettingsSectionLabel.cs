using Avalonia.Data.Converters;

namespace EmuShelf.App.ViewModels;

/// <summary>
/// Friendly display names for the settings navigation, so a compound enum name like
/// <c>TexturePacks</c> renders as "Texture Packs" instead of the raw value.
/// </summary>
public static class SettingsSectionLabel
{
    public static FuncValueConverter<SettingsSection, string> Converter { get; } = new(section => section switch
    {
        // "General" undersold what the section holds (library visibility, metadata, maintenance),
        // so both settings surfaces label it "Library". The enum member stays General for stable
        // field ids (general.*) and settings compatibility.
        SettingsSection.General => "Library",
        SettingsSection.TexturePacks => "Texture Packs",
        SettingsSection.ArtworkMetadata => "Artwork & Metadata",
        _ => section.ToString(),
    });
}
