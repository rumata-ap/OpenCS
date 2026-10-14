using CScore;
using CScore.Fire;
using CScore.Fire.Entities;
using OpenCS.Utilites;
using System.Linq;
using System.Text.Json;

namespace OpenCS.ViewModels;

/// <summary>
/// Графики R-проверки: T и γ — карты T3 с ползунком времени; σ и ε — вид НДС (<see cref="SectionPlotVM"/>)
/// на момент проверки, с контурами, сглаживанием, интерактивными фибрами и разрезом.
/// </summary>
public sealed class FireRCheckPlotsVM
{
    public FireMeshPlotVM? TemperaturePlot { get; }
    public FireMeshPlotVM? GammaPlot { get; }
    public SectionPlotVM? StressPlot { get; }
    public SectionPlotVM? StrainPlot { get; }

    /// <summary>Общий разрез вкладок σ и ε (σ — с γbt/γst по полю температур).</summary>
    public SectionCutVM? CutVM { get; }
    public bool HasPlots => TemperaturePlot != null;

    /// <summary>Почему графиков нет, если тепловой расчёт после проверки пересчитан.</summary>
    public string UnavailableText { get; } = "";
    public bool HasUnavailableText => UnavailableText.Length > 0;

    public FireRCheckPlotsVM(CalcResult result, CalcTask? task, AppViewModel app)
    {
        if (FireResultJson.TryGetError(result.DataJson, out _))
            return;

        JsonElement root = FireResultJson.Root(result.DataJson);
        JsonElement d = FireResultJson.Details(root);

        int thermalId = (int)FireResultJson.Dbl(d, "thermal_result_id");
        if (thermalId <= 0)
            return;

        // Пересчёт теплового расчёта заменяет строку с новым id, поэтому по старому id
        // чужое поле загрузиться не может — его просто нет.
        if (!app.db.FireThermalResultExists(thermalId))
        {
            UnavailableText = Loc.S("FireRCheck_ThermalReplaced");
            return;
        }

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

        section.ResolveAndBuildDiagramms(app.CalcSettings.Sp63DescEtaMin, pool: app.Diagrams,
            rebarDifferentialDiagram: app.CalcSettings.RebarDifferentialDiagram, ekbEtaMin: app.CalcSettings.EkbDescEtaMin);

        int snapIdx = (int)FireResultJson.Dbl(d, "snapshot_index", -1);
        var fiber = FireFiberSection.FromThermalResult(thermal, section, snapIdx);

        TemperaturePlot = FireMeshPlotBuilder.CreateTemperaturePlot(thermal, snapIdx);
        GammaPlot = FireMeshPlotBuilder.CreateGammaPlot(fiber, thermal, snapIdx);

        double e0 = FireResultJson.Dbl(d, "eps0");
        double ky = FireResultJson.Dbl(d, "ky", FireResultJson.Dbl(d, "kappa_x"));
        double kz = FireResultJson.Dbl(d, "kz", FireResultJson.Dbl(d, "kappa_y"));
        var k = new Kurvature { e0 = e0, ky = ky, kz = kz };
        var calc = task?.CalcType ?? CalcType.C;

        // Отдельный экземпляр: ползунок карты γ переключает снапшоты у своего FireFiberSection,
        // а σ, ε и разрез должны оставаться на моменте проверки.
        var stateFiber = FireFiberSection.FromThermalResult(thermal, section, snapIdx);
        StressPlot = FireSectionStatePlotBuilder.Build(SectionPlotMode.Stress, stateFiber, thermal, section, k, calc, app.CalcSettings);
        StrainPlot = FireSectionStatePlotBuilder.Build(SectionPlotMode.Strain, stateFiber, thermal, section, k, calc, app.CalcSettings);
        CutVM = new SectionCutVM(section, k, calc, app.FileDialogService, ten: false, stressSource: stateFiber)
        {
            WindowTitleSuffix = $"{task?.Tag} — {section.Tag}"
        };
        StressPlot.CutVM = CutVM;
        StrainPlot.CutVM = CutVM;
    }
}
