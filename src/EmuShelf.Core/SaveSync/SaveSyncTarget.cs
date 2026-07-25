namespace EmuShelf.Core.SaveSync;

/// <summary>Pairs one emulator-aware provider with the local endpoint that handles its units.</summary>
public sealed record SaveSyncTarget(ISaveLocationProvider Provider, ILocalSaveEndpoint LocalEndpoint);
