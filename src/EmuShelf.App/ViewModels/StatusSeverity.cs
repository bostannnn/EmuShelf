namespace EmuShelf.App.ViewModels;

/// <summary>
/// How long a status message earns on screen, and how it is drawn. The library toast is the only
/// surface for operational feedback, so the three kinds are deliberately distinct: a result the
/// user can miss without consequence, a running commentary that its own operation replaces, and a
/// failure that must survive long enough to be read.
/// </summary>
public enum StatusSeverity
{
    /// <summary>A completed action's result. Expires on its own.</summary>
    Info,

    /// <summary>Commentary on work still running. Never expires — the operation replaces it.</summary>
    Progress,

    /// <summary>Something failed. Stays long enough to read, then expires.</summary>
    Error,
}
