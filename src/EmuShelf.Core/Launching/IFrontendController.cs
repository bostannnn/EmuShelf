namespace EmuShelf.Core.Launching;

/// <summary>Platform/UI boundary for minimizing EmuShelf during play and restoring it afterward.</summary>
public interface IFrontendController
{
    /// <summary>Begins a tracked game session. Legacy implementations may map this to minimize.</summary>
    void SuspendForGame() => Minimize();

    /// <summary>Restores a tracked game session and returns input focus to the frontend.</summary>
    void ResumeAfterGame() => Restore();

    void Minimize();
    void Restore();
}
