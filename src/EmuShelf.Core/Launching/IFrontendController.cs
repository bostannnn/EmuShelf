namespace EmuShelf.Core.Launching;

/// <summary>Platform/UI boundary for minimizing EmuShelf during play and restoring it afterward.</summary>
public interface IFrontendController
{
    void Minimize();
    void Restore();
}
