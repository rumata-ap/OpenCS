using CScore.Fire;
using CScore.Fire.Entities;
using OpenCS.Utilites;
using System.Collections.ObjectModel;
using System.Globalization;

namespace OpenCS.ViewModels;

/// <summary>ViewModel результата теплового расчёта огневого сечения.</summary>
public sealed class FireThermalResultVM : ViewModelBase
{
    readonly FireThermalResult? _thermal;
    bool _temperaturePlotLoaded;
    bool _rebarChartLoaded;
    bool _convergenceChartsLoaded;

    public bool HasResult { get; }
    public string NoResultText { get; }
    public string HeaderText { get; }
    public string ResultIdText { get; }

    public string FireSectionText { get; }
    public string FireCurveText { get; }
    public string FireDurationText { get; }
    public string AggregateTypeText { get; }
    public string MeshNodesText { get; }
    public string MeshElementsText { get; }
    public string SnapshotsText { get; }
    public string MaxTemperatureText { get; }
    public string ConvergenceSummaryText { get; }

    public FireMeshPlotVM? TemperaturePlot { get; private set; }
    public FireLineChartVM? RebarChart { get; private set; }
    public FireLineChartVM? PicardChart { get; private set; }
    public FireLineChartVM? ResidualChart { get; private set; }
    public bool HasRebarChart => RebarChart is { Series.Count: > 0 };
    public bool HasConvergenceCharts => PicardChart is { Series.Count: > 0 };

    public bool IsLoading { get; private set; }

    public ObservableCollection<RebarTempRow> RebarRows { get; } = [];

    public sealed record RebarTempRow(string IdText, string MaxTempText);

    public FireThermalResultVM(FireSectionDef fireSection, FireThermalResult? thermal, int? resultId, bool loading = false)
    {
        IsLoading = loading;
        HeaderText = fireSection.Tag;

        if (thermal is null || resultId is null)
        {
            HasResult = false;
            NoResultText = IsLoading ? Loc.S("FireThermal_Loading") : Loc.S("FireThermal_NoResult");
            ResultIdText = "—";
            FireSectionText = fireSection.Tag;
            FireCurveText = MapCurve(fireSection.FireCurve);
            FireDurationText = fireSection.FireDurationMin.ToString("G", CultureInfo.InvariantCulture);
            AggregateTypeText = "—";
            MeshNodesText = MeshElementsText = SnapshotsText = MaxTemperatureText = "—";
            ConvergenceSummaryText = "—";
            return;
        }

        _thermal = thermal;
        HasResult = true;
        NoResultText = "";
        ResultIdText = resultId.Value.ToString(CultureInfo.InvariantCulture);

        var mesh = thermal.MeshInfo.Mesh;
        int nNodes = mesh.X.Length;
        int nElems = mesh.Elements.Length;
        int nSnap = thermal.Snapshots.Length;
        double maxT = thermal.Snapshots.Length > 0 && thermal.Snapshots[^1].Length > 0
            ? thermal.Snapshots[^1].Max()
            : 20.0;

        FireSectionText = fireSection.Tag;
        FireCurveText = MapCurve(thermal.FireCurve);
        FireDurationText = thermal.FireDurationMin.ToString("G", CultureInfo.InvariantCulture);
        AggregateTypeText = MapAggregate(thermal.AggregateType);
        MeshNodesText = nNodes.ToString(CultureInfo.InvariantCulture);
        MeshElementsText = nElems.ToString(CultureInfo.InvariantCulture);
        SnapshotsText = nSnap.ToString(CultureInfo.InvariantCulture);
        MaxTemperatureText = string.Format(CultureInfo.InvariantCulture, "{0:F1} °C", maxT);

        foreach (var kv in thermal.RebarMaxTemperatures.OrderBy(k => k.Key))
        {
            RebarRows.Add(new RebarTempRow(
                IdText: kv.Key.ToString(CultureInfo.InvariantCulture),
                MaxTempText: string.Format(CultureInfo.InvariantCulture, "{0:F1} °C", kv.Value)));
        }

        var log = thermal.ConvergenceLog;
        if (log.Count == 0)
            ConvergenceSummaryText = Loc.S("FireThermal_NoConvergenceLog");
        else
        {
            int maxPicard = log.Max(e => e.NPicardIter);
            double maxRes = log.Max(e => e.MaxResidualCelsius);
            ConvergenceSummaryText = string.Format(
                Loc.S("FireThermal_ConvergenceFormat"),
                log.Count, maxPicard, maxRes);
        }
    }

    /// <summary>Создать VM-заглушку на время фоновой загрузки результата.</summary>
    public static FireThermalResultVM CreateLoading(FireSectionDef fireSection)
        => new(fireSection, null, null, loading: true);

    /// <summary>Создать карту температуры при первом открытии вкладки поля.</summary>
    public FireMeshPlotVM? EnsureTemperaturePlotLoaded()
    {
        if (_thermal is null || _temperaturePlotLoaded)
            return TemperaturePlot;

        _temperaturePlotLoaded = true;
        TemperaturePlot = FireMeshPlotBuilder.CreateTemperaturePlot(_thermal);
        OnPropertyChanged(nameof(TemperaturePlot));
        return TemperaturePlot;
    }

    /// <summary>Создать график арматуры при первом открытии вкладки.</summary>
    public void EnsureRebarChartLoaded()
    {
        if (_thermal is null || _rebarChartLoaded)
            return;

        _rebarChartLoaded = true;
        RebarChart = FireMeshPlotBuilder.CreateRebarChart(_thermal);
        OnPropertyChanged(nameof(RebarChart));
        OnPropertyChanged(nameof(HasRebarChart));
    }

    /// <summary>Создать графики сходимости при первом открытии вкладки.</summary>
    public void EnsureConvergenceChartsLoaded()
    {
        if (_thermal is null || _convergenceChartsLoaded)
            return;

        _convergenceChartsLoaded = true;
        (PicardChart, ResidualChart) = FireMeshPlotBuilder.CreateConvergenceCharts(_thermal);
        OnPropertyChanged(nameof(PicardChart));
        OnPropertyChanged(nameof(ResidualChart));
        OnPropertyChanged(nameof(HasConvergenceCharts));
    }

    static string MapCurve(string curve) => curve switch
    {
        "iso834" => Loc.S("FireSection_CurveIso834"),
        "hydrocarbon" => Loc.S("FireSection_CurveHydrocarbon"),
        "slow" => Loc.S("FireSection_CurveSlow"),
        _ => curve
    };

    static string MapAggregate(string agg) => agg switch
    {
        "silicate" => Loc.S("FireResult_AggregateSilicate"),
        "carbonate" => Loc.S("FireResult_AggregateCarbonate"),
        _ => agg
    };
}
