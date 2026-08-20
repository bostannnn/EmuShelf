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
    bool GrantReadUriPermission)
{
    /// <summary>The explicit <c>package/activity</c> component this intent targets.</summary>
    public string Component => $"{PackageName}/{ActivityName}";
}
