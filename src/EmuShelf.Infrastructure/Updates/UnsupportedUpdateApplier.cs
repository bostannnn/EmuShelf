using EmuShelf.Core.Updates;

namespace EmuShelf.Infrastructure.Updates;

/// <summary>The fallback applier for platforms without an in-place update path (e.g. a raw dev run,
/// or an OS/arch with no published artifact). It never claims to be able to apply an update.</summary>
public sealed class UnsupportedUpdateApplier : IUpdateApplier
{
    private readonly string _reason;

    public UnsupportedUpdateApplier(string? reason = null) =>
        _reason = string.IsNullOrWhiteSpace(reason)
            ? "This build can't install updates itself."
            : reason;

    public bool CanApply(out string? reason)
    {
        reason = _reason;
        return false;
    }

    public void ApplyAndRelaunch(StagedUpdate staged) =>
        throw new InvalidOperationException(_reason);
}
