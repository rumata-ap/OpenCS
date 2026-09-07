using System;
using System.Collections.Generic;
using System.Linq;
using CScore.Planar;
using CScore.PlateStrip;
using OpenCS.OpenSees.CScore;
using OpenCS.OpenSees.Structural;
using Xunit;

namespace OpenCS.OpenSees.Tests
{
    /// <summary>Срез 7, Task 10: эпюра полосы из реальных shell resultants.</summary>
    public class ShellStripResultantSamplerTests
    {
        const double LengthM = 6.0;
        const double WidthM = 2.0;

        [Fact]
        public void UniformMxField_GivesMyEqualToMxTimesWidth()
        {
            // Прямая проверка того, что сумма весов равна ширине полосы.
            const double mxPerMetreN = 22_500.0;   // Н·м/м
            var snapshot = Grid(6, 2);
            var resultants = AllElements(snapshot, mx: mxPerMetreN);

            var epure = Sample(snapshot, resultants, Stations(6));

            foreach (double my in epure.Skip(1).Take(5).Select(s => s[1]))
                Assert.Equal(22.5 * WidthM, my, 6);   // кН·м после конверсии Н -> кН
        }

        [Fact]
        public void RotatedStrip_MatchesAnalyticResult()
        {
            // Полоса вдоль оси Y региона: без тензорного поворота Nx и Ny перепутались бы.
            // Это защита от класса ошибок Среза 3b, ловившегося только на неосеориентированной
            // геометрии.
            var snapshot = Grid(2, 6, width: 2.0, length: 6.0, swapAxes: true);
            var analogy = Analogy(stripFrame: new Frame3D(
                new PlanarVector3(0, 0, 0),
                new PlanarVector3(0, 1, 0),
                new PlanarVector3(-1, 0, 0),
                new PlanarVector3(0, 0, 1)));

            // В осях региона полоса тянется вдоль Y, поэтому продольное усилие полосы сидит в Ny.
            var resultants = AllElements(snapshot, ny: 10_000.0);

            var epure = ShellStripResultantSampler.Sample(
                resultants, TagMap(snapshot), snapshot, Frame3D.Identity, analogy, Stations(6),
                out var diagnostics);

            Assert.DoesNotContain(diagnostics, d => d.IsError);
            foreach (double n in epure.Skip(1).Take(4).Select(s => s[0]))
                Assert.Equal(10.0 * WidthM, n, 6);
        }

        [Fact]
        public void LinearlyVaryingField_GivesLinearEpure()
        {
            var snapshot = Grid(6, 2);
            var resultants = snapshot.Elements.Select(e =>
            {
                var centroid = PlanarMeshElementCentroid.Centroid(e, snapshot.Nodes);
                return new ShellSectionResultants(
                    e.Index + 1, 0, 0, 0, 0, 1_000.0 * centroid.U, 0, 0, 0, 0);
            }).ToList();

            var epure = Sample(snapshot, resultants, Stations(6));

            // Значения на внутренних станциях обязаны расти линейно.
            double d1 = epure[2][1] - epure[1][1];
            double d2 = epure[3][1] - epure[2][1];
            Assert.Equal(d1, d2, 6);
            Assert.True(d1 > 0);
        }

        [Fact]
        public void ElementsOutsideCorridor_DoNotContribute()
        {
            // Сетка шире полосы: за пределами коридора вклад обязан обрезаться.
            var wide = Grid(6, 4, width: 4.0);
            var resultants = AllElements(wide, mx: 1_000.0);

            var epure = Sample(wide, resultants, Stations(6));

            // Полоса шириной 2 м из сетки шириной 4 м: My = 1 кН·м/м × 2 м.
            foreach (double my in epure.Skip(1).Take(4).Select(s => s[1]))
                Assert.Equal(1.0 * WidthM, my, 6);
        }

        [Fact]
        public void MultipleIntegrationPoints_AreAveraged()
        {
            var snapshot = Grid(2, 2);
            var resultants = new List<ShellSectionResultants>();
            foreach (var element in snapshot.Elements)
            {
                resultants.Add(new(element.Index + 1, 0, 0, 0, 0, 1_000.0, 0, 0, 0, 0));
                resultants.Add(new(element.Index + 1, 1, 0, 0, 0, 3_000.0, 0, 0, 0, 0));
            }

            var epure = Sample(snapshot, resultants, Stations(2));

            // Среднее (1 + 3)/2 = 2 кН·м/м на ширину 2 м.
            Assert.Equal(2.0 * WidthM, epure[1][1], 6);
        }

        [Fact]
        public void StationWithoutElements_IsDiagnosed()
        {
            var snapshot = Grid(6, 2);
            var resultants = AllElements(snapshot, mx: 1_000.0);

            ShellStripResultantSampler.Sample(
                resultants, TagMap(snapshot), snapshot, Frame3D.Identity, Analogy(),
                [2.0], out var diagnostics);   // станция за пределами сетки

            Assert.Contains(diagnostics, d => d.Code == "plate_strip_shell_sampler_empty_station");
        }

        [Fact]
        public void UnknownElementTag_IsDiagnosed()
        {
            var snapshot = Grid(6, 2);
            var resultants = AllElements(snapshot, mx: 1_000.0);
            resultants.Add(new ShellSectionResultants(9999, 0, 0, 0, 0, 1_000.0, 0, 0, 0, 0));

            ShellStripResultantSampler.Sample(
                resultants, TagMap(snapshot), snapshot, Frame3D.Identity, Analogy(), Stations(6),
                out var diagnostics);

            Assert.Contains(diagnostics, d => d.Code == "plate_strip_shell_sampler_unknown_element");
        }

        [Fact]
        public void InvalidArguments_Throw()
        {
            var snapshot = Grid(2, 2);
            Assert.Throws<ArgumentNullException>(() => ShellStripResultantSampler.Sample(
                null!, TagMap(snapshot), snapshot, Frame3D.Identity, Analogy(), Stations(2), out _));
            Assert.Throws<ArgumentNullException>(() => ShellStripResultantSampler.Sample(
                [], TagMap(snapshot), null!, Frame3D.Identity, Analogy(), Stations(2), out _));
        }

        static double[][] Sample(
            PlanarMeshSnapshot snapshot, IReadOnlyList<ShellSectionResultants> resultants,
            IReadOnlyList<double> stations) =>
            ShellStripResultantSampler.Sample(
                resultants, TagMap(snapshot), snapshot, Frame3D.Identity, Analogy(), stations, out _);

        static List<ShellSectionResultants> AllElements(
            PlanarMeshSnapshot snapshot, double nx = 0, double ny = 0, double mx = 0) =>
            snapshot.Elements
                .Select(e => new ShellSectionResultants(e.Index + 1, 0, nx, ny, 0, mx, 0, 0, 0, 0))
                .ToList();

        static Dictionary<int, int> TagMap(PlanarMeshSnapshot snapshot) =>
            snapshot.Elements.ToDictionary(e => e.Index + 1, e => e.Index);

        static List<double> Stations(int count)
        {
            var stations = new List<double>(count + 1);
            for (int i = 0; i <= count; i++) stations.Add((double)i / count);
            return stations;
        }

        static PlateStripBeamAnalogy Analogy(Frame3D? stripFrame = null) => new()
        {
            Id = "strip-1",
            SourceRegionId = 1,
            ExplicitWidthM = WidthM,
            Fingerprint = "fp",
            StripFrame = stripFrame ?? Frame3D.Identity,
            Geometry = new PlateStripGeometry { LengthM = LengthM }
        };

        /// <summary>Регулярная четырёхугольная сетка nx × ny в координатах региона.</summary>
        static PlanarMeshSnapshot Grid(
            int nx, int ny, double width = WidthM, double length = LengthM, bool swapAxes = false)
        {
            double sizeU = (swapAxes ? width : length) / nx;
            double sizeV = (swapAxes ? length : width) / ny;
            var nodes = new List<PlanarMeshNode>();
            for (int j = 0; j <= ny; j++)
            for (int i = 0; i <= nx; i++)
            {
                // Поперечная координата полосы центрируется на нуле: у неповёрнутой полосы это
                // v региона, у повёрнутой (ось полосы вдоль Y) — u.
                double u = swapAxes ? i * sizeU - width / 2.0 : i * sizeU;
                double v = swapAxes ? j * sizeV : j * sizeV - width / 2.0;
                nodes.Add(new PlanarMeshNode(j * (nx + 1) + i, u, v, u, v, 0));
            }

            var elements = new List<PlanarMeshElement>();
            for (int j = 0; j < ny; j++)
            for (int i = 0; i < nx; i++)
            {
                int n0 = j * (nx + 1) + i;
                elements.Add(new PlanarMeshElement(
                    j * nx + i, PlanarMeshElementKind.Quadrangle4,
                    [n0, n0 + 1, n0 + nx + 2, n0 + nx + 1]));
            }

            return new PlanarMeshSnapshot
            {
                RegionId = 1, IsCalculable = true, Nodes = nodes, Elements = elements
            };
        }
    }
}
