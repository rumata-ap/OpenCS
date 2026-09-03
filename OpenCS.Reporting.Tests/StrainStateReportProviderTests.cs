using CScore;
using OpenCS.Reporting;
using Xunit;

namespace OpenCS.Reporting.Tests;

public sealed class StrainStateReportProviderTests
{
    [Fact]
    public void Provider_BuildsSp63SectionsAndFormulas()
    {
        var task = new CalcTask { Id = 1, Kind = "strain_state", Tag = "Колонна", CalcType = CalcType.C };
        var result = new CalcResult
        {
            TaskId = task.Id,
            TaskKind = task.Kind,
            TaskTag = task.Tag,
            Status = "ok",
            DataJson = """
                {
                  "converged": true, "iterations": 4, "residual": 0.01,
                  "e0": -0.0002, "ky": 0.001, "kz": -0.002,
                  "N_target": 100, "Mx_target": 20, "My_target": -30,
                  "N_result": 100, "Mx_result": 20, "My_result": -30,
                  "formula_version": "SP63.13330.2021/8.1",
                  "stiffness": { "source": "contour", "d11": 11, "d12": 12, "d13": 13, "d21": 12, "d22": 22, "d23": 23, "d31": 13, "d32": 23, "d33": 33 },
                  "jacobian": { "rows": ["N", "Mx", "My"], "columns": ["e0", "ky", "kz"], "scheme": "central", "h": 0.0000001, "values": [[1,2,3],[4,5,6],[7,8,9]] },
                  "equilibrium": { "n": 100, "mx": 20, "my": -30 },
                  "extrema": { "eps_b_min": -0.001, "eps_b_max": 0.002, "eps_s_min": -0.003, "eps_s_max": 0.004 },
                  "eta": {
                    "mode": "iterative", "mxOriginal": 18, "myOriginal": -27,
                    "l0x": 4, "hx": 0.3, "slendernessX": 13.33, "dX": 1.1, "etaX": 1,
                    "ncrX": null, "slenderX": false, "stableX": true,
                    "l0y": 5, "hy": 0.25, "slendernessY": 20, "dY": 2.2, "etaY": 1.12,
                    "ncrY": 80, "slenderY": true, "stableY": true,
                    "etaHistoryX": [], "etaHistoryY": [1.0, 1.08, 1.12]
                  },
                  "prestress": {
                    "reference": { "x_m": 0.01, "y_m": -0.02 },
                    "nominal": { "N_kN": 10, "Mx_kNm": 0.2, "My_kNm": -0.3 },
                    "effective": { "N_kN": 9, "Mx_kNm": 0.18, "My_kNm": -0.27 },
                    "actual": { "N_kN": 8.5, "Mx_kNm": 0.17, "My_kNm": -0.25 },
                    "hasGroupsAboveStrength": false,
                    "groups": [{
                      "areaId": 4, "tag": "Lower group", "area_m2": 0.0004,
                      "sigSp_MPa": 900, "gammaSp": 1, "sigActual_MPa": 850,
                      "sigLimit_MPa": 1000, "exceedsStrength": false,
                      "nominal": { "N_kN": 10, "Mx_kNm": 0.2, "My_kNm": -0.3 },
                      "effective": { "N_kN": 9, "Mx_kNm": 0.18, "My_kNm": -0.27 },
                      "actual": { "N_kN": 8.5, "Mx_kNm": 0.17, "My_kNm": -0.25 }
                    }]
                  },
                  "rebar": [
                    { "num": 1, "x_mm": -120, "y_mm": -180, "eps": 0.0012, "sigma_mpa": 240 },
                    { "num": 2, "x_mm": 120, "y_mm": -180, "eps": 0.0011, "sigma_mpa": 220 }
                  ]
                }
                """
        };

        var document = new StrainStateReportProvider().Build(
            new ReportContext(task, result, new Dictionary<string, string>
            {
                ["strain"] = "<svg data-kind=\"strain\"></svg>",
                ["stress"] = "<svg data-kind=\"stress\"></svg>"
            }));

        var headings = document.Blocks.OfType<ReportHeading>().Select(x => x.Text).ToList();
        var formulas = document.Blocks.OfType<ReportFormula>().Select(x => x.Reference).ToList();

        Assert.Contains("Исходные данные", headings);
        Assert.Contains("Плоскость деформаций", headings);
        Assert.Contains("Матрица жёсткости по СП 63", headings);
        Assert.Contains("Якобиан Ньютона", headings);
        Assert.Contains("(8.26)", formulas);
        Assert.Contains("(8.42)", formulas);
        Assert.Contains("(8.47)", formulas);
        Assert.Equal(2, document.Blocks.OfType<ReportImage>().Count());
        Assert.Contains("Влияние прогиба", headings);
        Assert.Contains("Преднапряжение", headings);
        Assert.Contains("Арматура", headings);
        var rebarTable = document.Blocks.OfType<ReportTable>()
            .Single(table => table.Headers.Contains("№"));
        Assert.Equal(2, rebarTable.Rows.Count);
    }

    [Fact]
    public void Provider_IncludesSectionGeometryMaterialsAndUnits_WhenSectionIsAvailable()
    {
        var concrete = CreateMaterial(1, "B25", MatType.Concrete);
        var steel = CreateMaterial(2, "A500", MatType.ReSteelF);
        var concreteArea = new MaterialArea
        {
            Id = 10,
            Num = 1,
            Tag = "Бетонная часть",
            Material = concrete,
            MaterialId = concrete.Id,
            DiagrammType = DiagrammType.L2,
            Contours =
            [
                new Contour(
                    [-0.15, 0.15, 0.15, -0.15, -0.15],
                    [-0.25, -0.25, 0.25, 0.25, -0.25],
                    "Hull") { Type = ContourType.Hull },
                new Contour(
                    [-0.03, 0.03, 0.03, -0.03, -0.03],
                    [-0.08, -0.08, 0.08, 0.08, -0.08],
                    "Hole") { Type = ContourType.Hole }
            ]
        };
        concreteArea.SetWKT();
        concreteArea.ResolveAndBuildDiagramms();

        var rebarArea = new MaterialArea
        {
            Id = 20,
            Num = 2,
            Tag = "Нижняя арматура",
            Category = AreaCategory.RebarGroup,
            Material = steel,
            MaterialId = steel.Id,
            DiagrammType = DiagrammType.L2,
            Fibers =
            [
                Fiber.CreatePoint(0.016, -0.11, -0.19),
                Fiber.CreatePoint(0.016, 0.11, -0.19)
            ]
        };
        rebarArea.ResolveAndBuildDiagramms();

        var section = new CrossSection
        {
            Id = 7,
            Num = 3,
            Tag = "Сечение колонны",
            Description = "Прямоугольное сечение с отверстием",
            Areas = [concreteArea, rebarArea]
        };
        var k = new Kurvature { e0 = -0.0002, ky = 0.001, kz = -0.002 };
        section.SetEps(k, CalcType.C);

        var task = new CalcTask
        {
            Id = 1,
            Num = 4,
            Kind = "strain_state",
            Tag = "Колонна К-1",
            SectionId = section.Id,
            CalcType = CalcType.C
        };
        var result = new CalcResult
        {
            Id = 99,
            TaskId = task.Id,
            TaskKind = task.Kind,
            TaskTag = task.Tag,
            Created = "2026-09-03 12:00",
            Status = "ok",
            DataJson = """
                {
                  "converged": true, "e0": -0.0002, "ky": 0.001, "kz": -0.002,
                  "N_target": 100, "Mx_target": 20, "My_target": -30,
                  "N_result": 100, "Mx_result": 20, "My_result": -30,
                  "formula_version": "SP63.13330.2021/8.1"
                }
                """
        };

        var document = new StrainStateReportProvider().Build(
            new ReportContext(task, result, section,
                new Dictionary<string, string>
                {
                    ["strain"] = "<svg width=\"900\" height=\"650\"></svg>",
                    ["stress"] = "<svg width=\"900\" height=\"650\"></svg>"
                }));

        var headings = document.Blocks.OfType<ReportHeading>().Select(x => x.Text).ToList();
        Assert.Contains("Идентификация и единицы", headings);
        Assert.Contains("Геометрия сечения", headings);
        Assert.Contains("Материалы и диаграммы", headings);
        Assert.Contains(document.Blocks.OfType<ReportParagraph>(), x => x.Text.Contains("кН"));
        Assert.Contains(document.Blocks.OfType<ReportTable>(), x => x.Headers.Contains("Материал"));
        Assert.Contains(document.Blocks.OfType<ReportTable>(), x =>
            x.Headers.Any(header => header.Contains("диаграмм", StringComparison.OrdinalIgnoreCase)));
        Assert.Contains(document.Blocks.OfType<ReportImage>(), x => x.Name.Contains("Геометрия"));
        Assert.Contains(document.Blocks.OfType<ReportImage>(), x => x.Name.Contains("σ(ε)"));
    }

    static Material CreateMaterial(int id, string tag, MatType type)
    {
        MaterialChars Chars(CalcType calc) => new(calc)
        {
            Type = type,
            E = type == MatType.Concrete ? 30000000 : 200000000,
            Fc = type == MatType.Concrete ? -14500 : -435000,
            Ft = type == MatType.Concrete ? 1050 : 435000,
            Ry = type == MatType.Concrete ? 0 : 435000,
            Ec0 = type == MatType.Concrete ? -0.002 : -0.002,
            Ec1Red = type == MatType.Concrete ? -0.0015 : -0.002175,
            Ec2 = type == MatType.Concrete ? -0.0035 : -0.003,
            Et1Red = type == MatType.Concrete ? 0.000035 : 0.002175,
            Et0 = type == MatType.Concrete ? 0.00007 : 0.002175,
            Et1 = type == MatType.Concrete ? 0.000035 : 0.001,
            Et2 = type == MatType.Concrete ? 0.00015 : 0.01
        };

        var c = Chars(CalcType.C);
        return new Material(id, tag, tag, type, c, Chars(CalcType.CL), Chars(CalcType.N), Chars(CalcType.NL));
    }
}
