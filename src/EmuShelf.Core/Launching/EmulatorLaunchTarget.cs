namespace EmuShelf.Core.Launching;

/// <summary>Describes an already-installed emulator without invoking a shell.</summary>
public abstract record EmulatorLaunchTarget;

/// <summary>A native executable, including an AppImage on Linux.</summary>
public sealed record DirectExecutableTarget(string Path) : EmulatorLaunchTarget;

/// <summary>An installed Flatpak application addressed by its stable application id.</summary>
public sealed record FlatpakApplicationTarget(string AppId) : EmulatorLaunchTarget;
