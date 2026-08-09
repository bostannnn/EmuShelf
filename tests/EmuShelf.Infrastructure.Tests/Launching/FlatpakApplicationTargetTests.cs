using EmuShelf.Core.Launching;

namespace EmuShelf.Infrastructure.Tests.Launching;

public sealed class FlatpakApplicationTargetTests
{
    [Fact]
    public void Ref_WithoutBranch_IsTheBareApplicationId()
    {
        var target = new FlatpakApplicationTarget("net.pcsx2.PCSX2");

        Assert.Equal("net.pcsx2.PCSX2", target.Ref);
        Assert.Null(target.Branch);
    }

    [Fact]
    public void Ref_WithBranch_IsBranchQualified()
    {
        var target = new FlatpakApplicationTarget("net.pcsx2.PCSX2", "beta");

        Assert.Equal("net.pcsx2.PCSX2//beta", target.Ref);
    }

    [Fact]
    public void Parse_BareApplicationId_HasNoBranch()
    {
        var target = FlatpakApplicationTarget.Parse("net.pcsx2.PCSX2");

        Assert.Equal(new FlatpakApplicationTarget("net.pcsx2.PCSX2"), target);
    }

    [Fact]
    public void Parse_BranchQualifiedRef_SplitsIdAndBranch()
    {
        var target = FlatpakApplicationTarget.Parse("net.pcsx2.PCSX2//beta");

        Assert.Equal(new FlatpakApplicationTarget("net.pcsx2.PCSX2", "beta"), target);
    }

    [Fact]
    public void Parse_TrailingSeparatorWithNoBranch_TreatedAsUnpinned()
    {
        var target = FlatpakApplicationTarget.Parse("net.pcsx2.PCSX2//");

        Assert.Equal(new FlatpakApplicationTarget("net.pcsx2.PCSX2"), target);
    }

    [Fact]
    public void Parse_RoundTripsThroughRef()
    {
        foreach (var reference in new[] { "net.pcsx2.PCSX2", "net.pcsx2.PCSX2//beta", "net.pcsx2.PCSX2//stable" })
            Assert.Equal(reference, FlatpakApplicationTarget.Parse(reference).Ref);
    }
}
