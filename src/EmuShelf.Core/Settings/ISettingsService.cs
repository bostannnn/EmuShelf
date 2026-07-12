namespace EmuShelf.Core.Settings;

public interface ISettingsService
{
    /// <summary>Loads settings from disk, or returns defaults if no settings file exists yet.</summary>
    AppSettings Load();

    void Save(AppSettings settings);
}
