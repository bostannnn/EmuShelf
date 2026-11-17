using System;

namespace EmuShelf.App.Views.Handheld;

/// <summary>
/// Where the UI side of the native screens' frame probe writes (M47 B6, R1's measurements): set by the platform only while its probe
/// switch is on (Android: <c>debug.emushelf.probe</c>, logcat tag <c>EmuShelfBigType</c>); null otherwise, and nothing is measured.
/// </summary>
public static class HandheldProbeLog
{
    public static Action<string>? Write { get; set; }
}
