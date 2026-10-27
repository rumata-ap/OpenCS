using CScore;
using CScore.History;
using OpenCS.Reporting;
using Xunit;

namespace OpenCS.Reporting.Tests;

/// <summary>Отчёт по истории нагружения стадийного сечения.</summary>
public sealed class LoadHistoryReportProviderTests
{
    [Fact]
    public void Build_HasInputSummaryChartAndShortTable()
    {
        var p = new LoadHistoryParams
        {
            Stages = [new() { Tag = "монтаж", Steps = 3, Force = new StageForce { Mode = "manual", Mx = -60 } },
                      new() { Tag = "усиление", ActivatePart = 1, Steps = 2, Force = new StageForce { Mode = "manual", Mx = -90 } }],
            PartRoles = [new() { PartIndex = 1, Concrete = PartLimitRole.Ignore, Rebar = PartLimitRole.Limits }],
        };
        var data = new LoadHistoryResultData
        {
            PartTags = ["балка", "обойма"], StageTags = ["монтаж", "усиление", "разрушение"],
            Steps =
            [
                new() { Stage = 0, Kind = "load", Step = 3, Parts = 1, Mx = -60, C = 2e-3, Mu = 60, Converged = true },
                new() { Stage = 1, Kind = "load", Step = 2, Parts = 2, Mx = -90, C = 3e-3, Mu = 90, Converged = true },
                new() { Stage = 2, Kind = "failure", Step = 5, Parts = 2, Mx = -150, C = 9e-3, Mu = 150, Converged = true, LimitPoint = true },
            ],
            With = new RunSummaryDto { MLim = 150, MPeak = 155, StopReason = "drop", GoverningPart = 0, GoverningMaterial = "concrete" },
            NonStandardRoles = [.. p.PartRoles],
        };
        var task = new CalcTask { Kind = "load_history", Tag = "Т1", CalcType = CalcType.C, ParamsJson = p.ToJson() };
        var result = new CalcResult { TaskKind = "load_history", Status = "ok", DataJson = data.ToJson() };

        var registry = ReportProviderRegistry.CreateDefault();
        Assert.True(registry.TryResolve(task, out var provider));
        var doc = provider.Build(new ReportContext(task, result));
        // ToString() записей не раскрывает таблицы — текст документа берём из Markdown-рендерера.
        var text = new MarkdownReportRenderer().Render(doc);
        Assert.Contains("монтаж", text);
        Assert.Contains("обойма", text);
        Assert.Contains(doc.Blocks, b => b is ReportImage img && img.Svg.Contains("<polyline"));
        Assert.Contains(doc.Blocks, b => b is ReportWarning w && w.Text.Contains("8.1.30"));
        var table = doc.Blocks.OfType<ReportTable>().Last();
        Assert.Equal(3, table.Rows.Count);                    // концы двух этапов + точка M_lim
        Assert.Contains("нелинейно-упругая", text);
        Assert.Contains("разгрузка идёт по кривой нагружения", text);
    }

    [Fact]
    public void Build_Hysteresis_MaterialModelRow_NoElasticNote()
    {
        var p = new LoadHistoryParams
        {
            Stages = [new() { Tag = "1", Steps = 2, Force = new StageForce { Mode = "manual", Mx = -60 } }],
        };
        var data = new LoadHistoryResultData
        {
            PartTags = ["балка"], StageTags = ["1"], StrainMode = "fibers",
            Steps = [new() { Stage = 0, Kind = "load", Step = 2, Parts = 1, Mx = -60, C = 2e-3, Mu = 60, Converged = true }],
            Hysteresis = new CScore.Hysteresis.HysteresisOptions(),
        };
        var task = new CalcTask { Kind = "load_history", Tag = "Т2", CalcType = CalcType.C, ParamsJson = p.ToJson() };
        var result = new CalcResult { TaskKind = "load_history", Status = "ok", DataJson = data.ToJson() };
        Assert.True(ReportProviderRegistry.CreateDefault().TryResolve(task, out var provider));
        var text = new MarkdownReportRenderer().Render(provider.Build(new ReportContext(task, result)));
        Assert.Contains("Karsan–Jirsa", text);
        Assert.Contains("Menegotto–Pinto", text);
        Assert.DoesNotContain("разгрузка идёт по кривой нагружения", text);
    }
}
