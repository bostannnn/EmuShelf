namespace EmuShelf.Core.Launching.Android;

/// <summary>
/// A fully-resolved Android launch intent, expressed as plain data so it can be built and asserted in
/// the desktop test suite (the plan's rule: intent construction is a pure function and lives in a
/// <c>net10.0</c> assembly, not behind an emulator). The Android head translates this one-to-one into a
/// framework <c>Android.Content.Intent</c> — set the component, the action, the data <see cref="DataUri"/>,
/// each extra, each category, and <c>FLAG_GRANT_READ_URI_PERMISSION</c> when <see cref="GrantReadUriPermission"/>.
/// </summary>
public sealed record AndroidIntentRequest(
    string PackageName,
    string ActivityName,
    string? Action,
    string? DataUri,
    IReadOnlyDictionary<string, string> StringExtras,
    IReadOnlyDictionary<string, bool> BoolExtras,
    IReadOnlyList<string> Categories,
    bool GrantReadUriPermission,
    string? RomContentUri = null)
{
    /// <summary>The explicit <c>package/activity</c> component this intent targets.</summary>
    public string Component => $"{PackageName}/{ActivityName}";

    /// <summary>
    /// True when the ROM travels as a <c>content://</c> URI in a <em>string extra</em> rather than the
    /// intent's data slot (Dolphin's <c>AutoStartFile</c>, DuckStation's <c>bootPath</c>, WatermelonDS's
    /// <c>uri</c>). A read grant follows the intent's data URI and its <c>ClipData</c>, never an arbitrary
    /// extra — so to delegate the grant for these the head must also attach the URI as <c>ClipData</c>.
    /// </summary>
    public bool RomUriRidesInExtra =>
        RomContentUri is not null &&
        !string.Equals(RomContentUri, DataUri, StringComparison.Ordinal);
}
