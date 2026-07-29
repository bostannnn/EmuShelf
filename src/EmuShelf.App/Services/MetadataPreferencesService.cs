using EmuShelf.Core.Settings;

namespace EmuShelf.App.Services;

public enum MetadataConsentChoice
{
    NotNow,
    FetchOnce,
    Always,
}

public interface IMetadataPreferencesService
{
    bool AutomaticallyFetchAfterImport { get; }
    bool ConsentPromptShown { get; }

    Task SaveAutomaticFetchAsync(
        bool enabled,
        CancellationToken cancellationToken = default);

    Task RecordConsentAsync(
        MetadataConsentChoice choice,
        CancellationToken cancellationToken = default);
}

public sealed class MetadataPreferencesService : IMetadataPreferencesService
{
    private readonly ISettingsService _settingsService;
    private AppSettings _settings;

    public bool AutomaticallyFetchAfterImport =>
        _settings.AutomaticallyFetchMetadataAfterImport;
    public bool ConsentPromptShown => _settings.MetadataConsentPromptShown;

    public MetadataPreferencesService(ISettingsService settingsService, AppSettings settings)
    {
        _settingsService = settingsService;
        _settings = settings;
    }

    public Task SaveAutomaticFetchAsync(
        bool enabled,
        CancellationToken cancellationToken = default) => SaveAsync(
        current => current with
        {
            AutomaticallyFetchMetadataAfterImport = enabled,
            MetadataConsentPromptShown = true,
        },
        cancellationToken);

    public Task RecordConsentAsync(
        MetadataConsentChoice choice,
        CancellationToken cancellationToken = default) => SaveAsync(
        current => current with
        {
            AutomaticallyFetchMetadataAfterImport = choice == MetadataConsentChoice.Always,
            MetadataConsentPromptShown = true,
        },
        cancellationToken);

    private async Task SaveAsync(
        Func<AppSettings, AppSettings> update,
        CancellationToken cancellationToken)
    {
        _settings = await Task.Run(
            () => _settingsService.Update(update),
            cancellationToken);
    }
}

internal sealed class NullMetadataPreferencesService : IMetadataPreferencesService
{
    public bool AutomaticallyFetchAfterImport => false;
    public bool ConsentPromptShown => true;

    public Task SaveAutomaticFetchAsync(bool enabled, CancellationToken cancellationToken = default) =>
        Task.CompletedTask;

    public Task RecordConsentAsync(
        MetadataConsentChoice choice,
        CancellationToken cancellationToken = default) => Task.CompletedTask;
}
