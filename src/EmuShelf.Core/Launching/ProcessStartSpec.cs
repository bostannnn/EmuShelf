namespace EmuShelf.Core.Launching;

/// <summary>One shell-free process invocation prepared by the launcher.</summary>
public sealed record ProcessStartSpec(
    string FileName,
    IReadOnlyList<string> Arguments,
    string WorkingDirectory);
