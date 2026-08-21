using EmuShelf.Core.Storage;

namespace EmuShelf.Infrastructure.Tests.Storage;

public class DataLocationResolverTests
{
    [Fact]
    public void Resolve_FirstRun_WhenNoPointerWritten()
    {
        var resolver = new DataLocationResolver(
            new FakeStore(pointer: null),
            new FakePermission(requiresGrant: true, isGranted: true),
            isWritable: _ => true);

        var result = resolver.Resolve();

        Assert.False(result.IsResolved);
        Assert.Equal(DataLocationOnboardingReason.FirstRun, result.OnboardingReason);
    }

    [Fact]
    public void Resolve_FirstRun_WhenPointerHasBlankBaseDirectory()
    {
        var resolver = new DataLocationResolver(
            new FakeStore(new DataLocation("   ")),
            new FakePermission(requiresGrant: true, isGranted: true),
            isWritable: _ => true);

        Assert.Equal(DataLocationOnboardingReason.FirstRun, resolver.Resolve().OnboardingReason);
    }

    [Fact]
    public void Resolve_StoragePermissionMissing_WhenGrantRequiredButNotHeld()
    {
        var resolver = new DataLocationResolver(
            new FakeStore(new DataLocation("/storage/AE6A-1092/EmuShelf")),
            new FakePermission(requiresGrant: true, isGranted: false),
            // Would be writable, but the missing grant must be reported first and distinctly.
            isWritable: _ => true);

        var result = resolver.Resolve();

        Assert.False(result.IsResolved);
        Assert.Equal(DataLocationOnboardingReason.StoragePermissionMissing, result.OnboardingReason);
    }

    [Fact]
    public void Resolve_LocationUnavailable_WhenFolderNotWritable()
    {
        var resolver = new DataLocationResolver(
            new FakeStore(new DataLocation("/storage/AE6A-1092/EmuShelf")),
            new FakePermission(requiresGrant: true, isGranted: true),
            isWritable: _ => false);

        Assert.Equal(DataLocationOnboardingReason.LocationUnavailable, resolver.Resolve().OnboardingReason);
    }

    [Fact]
    public void Resolve_Resolved_WhenPointerGrantAndWritabilityAllHold()
    {
        var resolver = new DataLocationResolver(
            new FakeStore(new DataLocation("/storage/AE6A-1092/EmuShelf")),
            new FakePermission(requiresGrant: true, isGranted: true),
            isWritable: _ => true);

        var result = resolver.Resolve();

        Assert.True(result.IsResolved);
        Assert.Equal("/storage/AE6A-1092/EmuShelf", result.BaseDirectory);
        Assert.Null(result.OnboardingReason);
    }

    [Fact]
    public void Resolve_IgnoresMissingGrant_WhenPlatformDoesNotRequireOne()
    {
        // Desktop: RequiresGrant is false, so IsGranted is never consulted as a re-onboarding trigger.
        var resolver = new DataLocationResolver(
            new FakeStore(new DataLocation("/data/portable")),
            new FakePermission(requiresGrant: false, isGranted: false),
            isWritable: _ => true);

        Assert.True(resolver.Resolve().IsResolved);
    }

    private sealed class FakeStore(DataLocation? pointer) : IDataLocationStore
    {
        public DataLocation? Read() => pointer;
        public void Write(DataLocation location) => throw new NotSupportedException();
        public void Clear() => throw new NotSupportedException();
    }

    private sealed class FakePermission(bool requiresGrant, bool isGranted) : IStoragePermissionService
    {
        public bool RequiresGrant => requiresGrant;
        public bool IsGranted => isGranted;
        public void RequestGrant()
        {
        }
    }
}
