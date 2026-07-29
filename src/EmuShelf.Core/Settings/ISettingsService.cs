namespace EmuShelf.Core.Settings;

public interface ISettingsService
{
    /// <summary>Loads settings from disk, or returns defaults if no settings file exists yet.</summary>
    AppSettings Load();

    void Save(AppSettings settings);

    /// <summary>
    /// Atomically reads the latest settings, applies one scoped change, and persists the result.
    /// Implementations backed by a shared file should override this so independent settings owners
    /// cannot overwrite one another between the read and write.
    /// </summary>
    AppSettings Update(Func<AppSettings, AppSettings> update)
    {
        ArgumentNullException.ThrowIfNull(update);
        var updated = update(Load());
        Save(updated);
        return updated;
    }
}
