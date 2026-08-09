using CScore.Planar;
using CScore.PlateStrip;
using Xunit;

namespace CScore.Tests.PlateStrip;

public sealed class BeamJunctionTests
{
    [Fact]
    public void BuildStart_ReturnsSameSupportLocusReferenceAsStrip()
    {
        var section = Section();

        var junction = BeamJunctionBuilder.BuildStart(section);

        Assert.Same(section.Strip.StartSupportLocus, junction.SupportLocus);
        Assert.Equal(BeamJunctionEnd.Start, junction.End);
        Assert.Equal(section.Strip.Id, junction.StripBeamId);
    }

    [Fact]
    public void BuildEnd_ReturnsSameSupportLocusReferenceAsStrip()
    {
        var section = Section();

        var junction = BeamJunctionBuilder.BuildEnd(section);

        Assert.Same(section.Strip.EndSupportLocus, junction.SupportLocus);
        Assert.Equal(BeamJunctionEnd.End, junction.End);
    }

    [Fact]
    public void Geometry3DAndStructuralMode_ReflectMutationOfUnderlyingSupportLocus()
    {
        var section = Section();
        var junction = BeamJunctionBuilder.BuildStart(section);
        var newFrame = new Frame3D(
            new PlanarVector3(1, 2, 3),
            Frame3D.Identity.LocalX, Frame3D.Identity.LocalY, Frame3D.Identity.LocalZ);

        section.Strip.StartSupportLocus.Frame = newFrame;
        section.Strip.StartSupportLocus.StructuralMode = BeamJunctionMode.Tie;

        Assert.Equal(newFrame.Origin, junction.Geometry3D.Origin);
        Assert.Equal(BeamJunctionMode.Tie, junction.StructuralMode);
    }

    [Fact]
    public void MeshMode_IsAlwaysNotMeshed()
    {
        var junction = BeamJunctionBuilder.BuildStart(Section());

        Assert.Equal(BeamJunctionMeshMode.NotMeshed, junction.MeshMode);
    }

    [Fact]
    public void BuildStart_RejectsNullSection()
    {
        Assert.Throws<ArgumentNullException>(() => BeamJunctionBuilder.BuildStart(null!));
    }

    [Fact]
    public void BuildStart_RejectsMissingStartSupportLocus()
    {
        var section = Section();
        section.Strip.StartSupportLocus = null!;

        Assert.Throws<InvalidOperationException>(() => BeamJunctionBuilder.BuildStart(section));
    }

    [Fact]
    public void BuildStart_RejectsMissingStrip()
    {
        var section = Section();
        section.Strip = null!;

        Assert.Throws<InvalidOperationException>(() => BeamJunctionBuilder.BuildStart(section));
    }

    static EquivalentSection Section()
    {
        var strip = new PlateStripBeamAnalogy
        {
            Id = "strip-1",
            SourceRegionId = 10,
            ExplicitWidthM = 2.0,
            Fingerprint = "strip-fp",
            Geometry = new PlateStripGeometry { LengthM = 6.0 },
            StartSupportLocus = new SupportLocus(),
            EndSupportLocus = new SupportLocus()
        };
        return new EquivalentSection { Strip = strip };
    }
}
