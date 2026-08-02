using System.Linq;
using CScore.Planar;
using CScore.Planar.Fragments;
using Xunit;

namespace CScore.Tests.Planar
{
    public class PlanarBoundaryActionSetScalingTests
    {
        [Fact]
        public void Scale_MultipliesForceAndKinematicSamplesByFactor()
        {
            var source = new PlanarBoundaryActionSet
            {
                SourceMode = PlanarBoundaryActionSourceMode.Template,
                ForceActions =
                [
                    new PlanarBoundaryForceAction
                    {
                        InterfaceId = "top",
                        DofMask = PlanarDofMask.UZ,
                        ReferencePoint = new PlanarVector3(1, 0, 0),
                        Samples =
                        [
                            new(0, new PlanarVector3(0, 0, 100), new PlanarVector3(0, 10, 0)),
                            new(1, new PlanarVector3(0, 0, 200), new PlanarVector3(0, 20, 0))
                        ]
                    }
                ],
                KinematicActions =
                [
                    new PlanarBoundaryKinematicAction
                    {
                        InterfaceId = "top",
                        DofMask = PlanarDofMask.UZ,
                        Samples = [new(0, new PlanarVector3(0, 0, 0.01), PlanarVector3.Zero)]
                    }
                ]
            };

            var scaled = PlanarBoundaryActionSetScaling.Scale(source, 0.5);

            Assert.Equal(PlanarBoundaryActionSourceMode.Template, scaled.SourceMode);
            var force = Assert.Single(scaled.ForceActions);
            Assert.Equal("top", force.InterfaceId);
            Assert.Equal(new PlanarVector3(0, 0, 50), force.Samples[0].ForcePerLength);
            Assert.Equal(new PlanarVector3(0, 0, 100), force.Samples[1].ForcePerLength);
            Assert.Equal(new PlanarVector3(0, 5, 0), force.Samples[0].MomentPerLength);
            Assert.Equal(new PlanarVector3(1, 0, 0), force.ReferencePoint);

            var kinematic = Assert.Single(scaled.KinematicActions);
            Assert.Equal(new PlanarVector3(0, 0, 0.005), kinematic.Samples[0].Displacement);
        }

        [Fact]
        public void Scale_ByZero_ZeroesAllSampleMagnitudes()
        {
            var source = new PlanarBoundaryActionSet
            {
                SourceMode = PlanarBoundaryActionSourceMode.Template,
                ForceActions =
                [
                    new PlanarBoundaryForceAction
                    {
                        InterfaceId = "bottom",
                        Samples = [new(0, new PlanarVector3(1, 1, 1), new PlanarVector3(1, 1, 1))]
                    }
                ]
            };

            var scaled = PlanarBoundaryActionSetScaling.Scale(source, 0.0);

            Assert.Equal(PlanarVector3.Zero, scaled.ForceActions.Single().Samples[0].ForcePerLength);
            Assert.Equal(PlanarVector3.Zero, scaled.ForceActions.Single().Samples[0].MomentPerLength);
        }
    }
}
