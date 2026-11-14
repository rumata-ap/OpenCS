using System.IO;
using System.Xml.Linq;
using CScore;
using OpenCS.Reporting;
using OpenCS.Reporting.Pandoc;
using OpenCS.Tasks;
using OpenCS.Utilites;
using Xunit;

namespace OpenCS.Tests;

/// <summary>Отчёт продавливания по образцу strength-web: сквозной путь задача → DataJson → документ → Markdown.</summary>
[Collection("Loc language")]
public sealed class PunchingReportTests
{
    static (CalcTask Task, CalcResult Result) Run(PunchingTaskParams p)
    {
        var task = new CalcTask { Id = 7, Num = 3, Tag = "Колонна К-1", Kind = PunchingRunner.Kind, CalcType = CalcType.C, ParamsJson = p.ToJson() };
        return (task, PunchingRunner.Run(task, CalcSettings.Default, null));
    }

    static PunchingTaskParams Gosha() => new()
    {
        Area = new PunchingAreaParams { Bx = 0.25, By = 0.8 },
        Slab = new PunchingSlabParams { H = 0.2, Ax = 0.04, Ay = 0.03, RbtOverride = 1050 },
        Reinforcement = new PunchingReinforcementParams { Enabled = true, Diameter = 0.006, Count = 2, Step = 0.05, RswOverride = 170_000 },
        Forces = new PunchingForceParams { F = 600, Mx = 30, My = 20 }
    };

    static IEnumerable<string> Texts(ReportDocument doc) => doc.Blocks.Select(b => b switch
    {
        ReportHeading h => h.Text,
        ReportParagraph p => p.Text,
        ReportWarning w => w.Text,
        _ => ""
    });

    [Fact]
    public void Registry_ResolvesPunching()
        => Assert.IsType<PunchingReportProvider>(ReportProviderRegistry.CreateDefault().Resolve(new CalcTask { Kind = "punching" }));

    [Fact]
    public void Build_SectionPerVariant_FullSubstitution_VerdictAndReserve()
    {
        var (task, result) = Run(Gosha());
        var doc = new PunchingReportProvider().Build(new ReportContext(task, result));
        var texts = Texts(doc).ToList();

        Assert.Contains(texts, t => t.StartsWith("Замкнутый контур (рис. 8.11а)", StringComparison.Ordinal));
        Assert.Contains(texts, t => t.StartsWith("Разорванный контур у торцов пилона", StringComparison.Ordinal) && t.EndsWith("определяющий", StringComparison.Ordinal));
        Assert.Contains(texts, t => t.Contains("запас Δ = (1 − K)·100 % = 7,3 %", StringComparison.Ordinal));
        Assert.Contains(texts, t => t.Contains("K = 1,019 > 1 — прочность НЕ обеспечена", StringComparison.Ordinal));
        Assert.Contains(texts, t => t.Contains("Kmax = 1,019 > 1", StringComparison.Ordinal));

        var fb = doc.Blocks.OfType<ReportCalculationStep>().First(s => s.StepId == "sp63.punching.closed.fb");
        // F_b,ult = Rbt·u·h0 = 1050 · 2,76 · 0,165 = 478,2 кН
        Assert.Equal(@"F_{b,ult} = R_{bt}\,u\,h_0 = 1050 \cdot 2{,}76 \cdot 0{,}165 = 478{,}2\ \text{кН}", fb.Formula.Source);

        var images = doc.Blocks.OfType<ReportImage>().ToList();
        Assert.Equal(3, images.Count);   // схема + панель на каждый из двух контуров
        foreach (var img in images) XDocument.Parse(img.Svg);
    }

    [Fact]
    public void Build_ErrorResult_ShowsReason()
    {
        var (task, result) = Run(Gosha() with { Slab = new PunchingSlabParams { H = 0 } });
        var doc = new PunchingReportProvider().Build(new ReportContext(task, result));
        Assert.Contains(Texts(doc), t => t.StartsWith("Расчёт не выполнен", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Markdown_ForPandoc_HasFormulasAndSvgAssets()
    {
        var p = Gosha() with
        {
            Edges = [new PunchingEdgeParams { Side = "+x", C = 0.3 }],
            Holes = [new PunchingHoleParams { Shape = "circle", Cx = 0, Cy = 0.75, D = 0.2 }]
        };
        var (task, result) = Run(p);
        var doc = new PunchingReportProvider().Build(new ReportContext(task, result));
        string dir = Path.Combine(Path.GetTempPath(), $"opencs_punch_rep_{Guid.NewGuid():N}");
        try
        {
            var source = await new PandocReportSourceRenderer().RenderAsync(doc, dir);
            string md = await File.ReadAllTextAsync(source.MarkdownPath);
            Assert.Contains("Сводка вариантов", md);
            Assert.Contains("$u = \\sum L_i = ", md);
            Assert.True(source.AssetPaths.Count >= 3);
        }
        finally
        {
            try { Directory.Delete(dir, recursive: true); } catch (IOException) { }
        }
    }
}
