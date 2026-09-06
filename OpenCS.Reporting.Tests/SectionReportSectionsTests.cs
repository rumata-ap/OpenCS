using CScore;
using OpenCS.Reporting;
using Xunit;

namespace OpenCS.Reporting.Tests;

/// <summary>Проверки общих разделов отчётов по сечению.</summary>
public sealed class SectionReportSectionsTests
{
    [Fact]
    public void GeometryAndRebar_AddExpectedBlocks()
    {
        var section = CreateSection();
        var k = new Kurvature { e0 = 0.0, ky = 0.001, kz = 0.0 };

        var geometry = SectionReportSections.Geometry(new ReportDocument("geometry"), section, k);
        Assert.Contains(geometry.Blocks, block => block is ReportImage);
        Assert.Equal(2, geometry.Blocks.OfType<ReportTable>().Count());

        var rebar = SectionReportSections.Rebar(new ReportDocument("rebar"), section, k, CalcType.C);
        Assert.Equal(2, rebar.Blocks.OfType<ReportTable>().Count());
        Assert.All(rebar.Blocks.OfType<ReportTable>(), table => Assert.Single(table.Rows));
    }

    [Fact]
    public void EmptyOptionalSections_DoNotAddBlocks_AndMissingSectionWarns()
    {
        var etaDocument = SectionReportSections.Eta(new ReportDocument("eta"), null);
        var prestressDocument = SectionReportSections.Prestress(new ReportDocument("prestress"), null);

        Assert.Empty(etaDocument.Blocks);
        Assert.Empty(prestressDocument.Blocks);

        var warningDocument = SectionReportSections.SectionMissingWarning(new ReportDocument("warning"));
        Assert.Contains(warningDocument.Blocks, block => block is ReportWarning);
    }

    [Fact]
    public void Eta_PreservesNullValuesAndPrintsStabilitySeparately()
    {
        var eta = new EtaReportData
        {
            Mode = "iterative",
            EtaX = null,
            StableX = false,
            EtaY = 1.12
        };

        var document = SectionReportSections.Eta(new ReportDocument("eta"), eta);
        var table = Assert.Single(document.Blocks.OfType<ReportTable>(), x => x.Headers.Contains("η"));

        Assert.Equal("—", table.Rows[0][3]);
        Assert.Equal("неустойчиво", table.Rows[0][4]);
        Assert.Equal("1.12", table.Rows[1][3]);
    }

    static CrossSection CreateSection()
    {
        var concrete = new MaterialArea
        {
            Id = 1,
            Tag = "Бетон",
            Contours =
            [
                new Contour(
                    [-0.1, 0.1, 0.1, -0.1, -0.1],
                    [-0.2, -0.2, 0.2, 0.2, -0.2],
                    "Hull") { Type = ContourType.Hull }
            ]
        };
        concrete.SetWKT();

        var rebar = new MaterialArea
        {
            Id = 2,
            Tag = "Арматура",
            Category = AreaCategory.RebarGroup,
            Fibers = [Fiber.CreatePoint(0.01, 0.0, -0.1)]
        };

        return new CrossSection { Areas = [concrete, rebar] };
    }
}
