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
        SettingsSection.TexturePacks => "Texture Packs",
        _ => section.ToString(),
    });
}
