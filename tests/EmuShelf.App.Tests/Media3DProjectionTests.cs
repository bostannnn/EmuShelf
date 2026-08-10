using System;
using System.Linq;
using EmuShelf.App.Controls;
using Xunit;

namespace EmuShelf.App.Tests;

// Pure geometry/culling/sort for the couch 3D hero. The Skia rasterization is screenshot-verified
// (ShelfRenderHarness); this locks the math the doc asked to keep testable headless.
public class Media3DProjectionTests
{
    [Theory]
    [InlineData("snes", MediaType.SnesCartridge)]
    [InlineData("playstation2", MediaType.Ps2KeepCase)]
    public void ForSystem_MapsTheAuthoredArchetypes(string systemId, MediaType expected) =>
        Assert.Equal(expected, Media3DProjection.ForSystem(systemId));

    [Theory]
    [InlineData("nes")]
    [InlineData("gamecube")]
    [InlineData("playstation")]
    [InlineData("arcade")]
    public void ForSystem_FallsBackToFlatForEveryOtherSystem(string systemId) =>
        Assert.Null(Media3DProjection.ForSystem(systemId));

    [Fact]
    public void FaceOn_ShowsOnlyTheFrontFace()
    {
        var faces = Media3DProjection.Project(MediaType.Ps2KeepCase, yaw: 0, pitch: 0, width: 400, height: 500);
        var only = Assert.Single(faces);
        Assert.Equal(MediaFace.Front, only.Face);
    }

    [Fact]
    public void FaceOn_FrontIsUprightAndHorizontallyCentred()
    {
        var faces = Media3DProjection.Project(MediaType.Ps2KeepCase, 0, 0, width: 400, height: 500);
        var front = faces.Single(f => f.Face == MediaFace.Front);
        // Corners are TL, TR, BR, BL.
        Assert.Equal(200, (front.Screen[0].X + front.Screen[1].X) / 2, 3); // top edge centred on width/2
        Assert.Equal(200, (front.Screen[2].X + front.Screen[3].X) / 2, 3); // bottom edge centred
        Assert.True(front.Screen[0].X < front.Screen[1].X);                 // TL left of TR
        Assert.True(front.Screen[0].Y < front.Screen[3].Y);                 // TL above BL
    }

    [Fact]
    public void Turning_RevealsExactlyOneSideFaceBesideTheFront()
    {
        var faces = Media3DProjection.Project(MediaType.Ps2KeepCase, yaw: 0.4, pitch: 0, 400, 500);
        Assert.Equal(2, faces.Count);
        Assert.Contains(faces, f => f.Face == MediaFace.Front);
        Assert.Contains(faces, f => f.Face is MediaFace.Left or MediaFace.Right);
    }

    [Fact]
    public void TurnedAway_CullsTheFrontAndShowsTheBack()
    {
        var faces = Media3DProjection.Project(MediaType.Ps2KeepCase, yaw: Math.PI, pitch: 0, 400, 500);
        Assert.Contains(faces, f => f.Face == MediaFace.Back);
        Assert.DoesNotContain(faces, f => f.Face == MediaFace.Front);
    }

    [Fact]
    public void PainterSort_OrdersFacesFarthestFirst()
    {
        var faces = Media3DProjection.Project(MediaType.SnesCartridge, yaw: 0.6, pitch: 0.25, 400, 500);
        Assert.True(faces.Count >= 2);
        for (var i = 1; i < faces.Count; i++)
            Assert.True(faces[i - 1].Depth >= faces[i].Depth, "faces must be sorted farthest-first");
    }

    [Fact]
    public void Culling_NeverKeepsMoreThanThreeFacesOfAConvexBox()
    {
        for (var deg = 0; deg < 360; deg += 15)
        {
            var faces = Media3DProjection.Project(MediaType.SnesCartridge, deg * Math.PI / 180, 0.3, 400, 500);
            Assert.InRange(faces.Count, 1, 3);
        }
    }
}
