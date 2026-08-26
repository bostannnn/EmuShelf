namespace EmuShelf.Core.Launching.Android;

/// <summary>
/// A fully-resolved Android launch intent, expressed as plain data so it can be built and asserted in
/// the desktop test suite (the plan's rule: intent construction is a pure function and lives in a
/// <c>net10.0</c> assembly, not behind an emulator). The Android head translates this one-to-one into a
/// framework <c>Android.Content.Intent</c> — set the component, the action, the data <see cref="DataUri"/>,
/// each extra and each category — and adds <c>FLAG_ACTIVITY_CLEAR_TASK</c> + <c>FLAG_ACTIVITY_CLEAR_TOP</c>
/// when <see cref="ClearTask"/>.
///
/// It deliberately carries no read-grant flag. Every emulator EmuShelf targets reads the ROM through its own
/// persisted <c>roms/&lt;system&gt;</c> SAF grant (RetroArch reads a plain path), so EmuShelf holds no grant
/// to delegate — attaching <c>FLAG_GRANT_READ_URI_PERMISSION</c> to a URI it does not own is exactly the
/// <c>SecurityException: Permission Denial</c> the port hit and removed. See <c>AndroidGameLauncher.Launch</c>
/// and DECISIONS 2026-08-26.
/// </summary>
public sealed record AndroidIntentRequest(
    string PackageName,
    string ActivityName,
    string? Action,
    string? DataUri,
    IReadOnlyDictionary<string, string> StringExtras,
    IReadOnlyDictionary<string, bool> BoolExtras,
    IReadOnlyList<string> Categories,
    bool ClearTask = false)
{
    /// <summary>The explicit <c>package/activity</c> component this intent targets.</summary>
    public string Component => $"{PackageName}/{ActivityName}";
}
