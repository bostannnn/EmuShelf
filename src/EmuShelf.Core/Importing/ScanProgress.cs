namespace EmuShelf.Core.Importing;

/// <summary>Progress report from a folder scan, surfaced in the status bar.</summary>
public readonly record struct ScanProgress(int CandidatesFound, string? CurrentDirectory);
