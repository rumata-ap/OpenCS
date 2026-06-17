using CScore;
using CScore.Fire;
using CScore.Fire.Entities;
using OpenCS.Utilites;
using System.Linq;
using System.Text.Json;

namespace OpenCS.ViewModels;

/// <summary>Графики R-проверки (T, γ, σ, ε) на чистом WPF.</summary>
public sealed class FireRCheckPlotsVM
{
    public FireMeshPlotVM? TemperaturePlot { get; }
    public FireMeshPlotVM? GammaPlot { get; }
    public FireMeshPlotVM? StressPlot { get; }
    public FireMeshPlotVM? StrainPlot { get; }
    public bool HasPlots => TemperaturePlot != null;

    public FireRCheckPlotsVM(CalcResult result, CalcTask? task, AppViewModel app)
    {
        if (FireResultJson.TryGetError(result.DataJson, out _))
            return;

        JsonElement root = FireResultJson.Root(result.DataJson);
        JsonElement d = FireResultJson.Details(root);

        int thermalId = (int)FireResultJson.Dbl(d, "thermal_result_id");
        if (thermalId <= 0)
            return;

        FireThermalResult thermal;
        try
        {
            thermal = app.db.LoadFireThermalResult(thermalId);
        }
        catch
        {
            return;
        }

        CrossSection? section = task != null
            ? app.CrossSections.FirstOrDefault(s => s.Id == task.SectionId)
            : null;
        if (section is null)
            return;

        section.ResolveAndBuildDiagramms(pool: app.Diagrams);

        int snapIdx = (int)FireResultJson.Dbl(d, "snapshot_index", -1);
        var fiber = FireFiberSection.FromThermalResult(thermal, section, snapIdx);

        TemperaturePlot = FireMeshPlotBuilder.CreateTemperaturePlot(thermal, snapIdx);
        GammaPlot = FireMeshPlotBuilder.CreateGammaPlot(fiber, thermal, snapIdx);

        double e0 = FireResultJson.Dbl(d, "eps0");
        double ky = FireResultJson.Dbl(d, "ky", FireResultJson.Dbl(d, "kappa_x"));
        double kz = FireResultJson.Dbl(d, "kz", FireResultJson.Dbl(d, "kappa_y"));
        var k = new Kurvature { e0 = e0, ky = ky, kz = kz };
        var calc = task?.CalcType ?? CalcType.C;

        StressPlot = FireMeshPlotBuilder.CreateStressPlot(fiber, thermal, k, calc, snapIdx);
        StrainPlot = FireMeshPlotBuilder.CreateStrainPlot(fiber, thermal, k, snapIdx);
    }
}
