using EmuShelf.Core.Handheld;

namespace EmuShelf.App.ViewModels;

/// <summary>
/// The look Handheld mode starts in (M47): the saved setting (<c>AppSettings.HandheldLook</c>), unless a developer override is
/// set for this run. The override is never saved; on Android it comes from the volatile system property
/// <c>debug.emushelf.look</c>, read once at start-up, so the Big Type look can be shown on the Thor without editing the
/// owner's settings.
/// </summary>
public static class HandheldLookChoice
{
    /// <summary>This run's look, whatever the settings say; null follows the settings.</summary>
    public static HandheldLook? Override { get; set; }

    /// <summary>The look to start in, given the saved one.</summary>
    public static HandheldLook Resolve(HandheldLook saved) => Override ?? saved;

    /// <summary>Reads an override's text: <c>bigtype</c> or <c>classic</c> (any case); anything else is no override.</summary>
    public static HandheldLook? Parse(string? text) => text?.Trim().ToLowerInvariant() switch
    {
        "bigtype" or "big-type" or "big type" => HandheldLook.BigType,
        "classic" => HandheldLook.Classic,
        _ => null,
    };
}
