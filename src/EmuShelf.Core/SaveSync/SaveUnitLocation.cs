namespace EmuShelf.Core.SaveSync;

/// <summary>
/// A provider-approved local destination for one save unit. <paramref name="RootPath"/> is the
/// boundary the generic filesystem endpoint enforces; <paramref name="Path"/> must be that root or
/// one of its descendants. Absolute paths never become cloud identities.
/// </summary>
public sealed record SaveUnitLocation(
    string Path,
    string RootPath,
    SaveUnitKind Kind);
