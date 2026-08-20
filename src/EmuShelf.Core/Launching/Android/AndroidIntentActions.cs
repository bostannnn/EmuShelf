namespace EmuShelf.Core.Launching.Android;

/// <summary>
/// The intent-action strings the launch profiles reference. Duplicated as literals here (rather than
/// used via <c>Android.Content.Intent.ActionView</c>) so the profiles and their tests live in the pure
/// <c>net10.0</c> Core/Integrations assemblies; the Android head passes these strings straight through.
/// </summary>
public static class AndroidIntentActions
{
    /// <summary><c>android.intent.action.VIEW</c>.</summary>
    public const string View = "android.intent.action.VIEW";

    /// <summary><c>android.intent.action.MAIN</c>.</summary>
    public const string Main = "android.intent.action.MAIN";
}
