using System.Windows.Media;
using OpenCS.Utilites;

namespace OpenCS.ViewModels;

/// <summary>
/// Переиспользуемый блок «Трещинообразование» — общий для задач cracking и crack_width
/// (последняя считает момент трещинообразования тем же CrackingSolver внутри себя).
/// </summary>
public sealed class CrackingMomentPartVM
{
    public string NText { get; }
    public string MxCrcText { get; }
    public string MyCrcText { get; }
    public string McrcText { get; }
    public string StatusText { get; }
    public Brush StatusBrush { get; }
    public bool HasEpsData { get; }
    public string EpsMaxTensionText { get; } = "—";
    public string EpsTensionLimitText { get; } = "—";
    /// <summary>Отношение приложенного момента к Mcrc (только для crack_width — там есть с чем сравнивать).</summary>
    public bool HasUtilization { get; }
    public string UtilizationText { get; } = "—";

    public CrackingMomentPartVM(double n, double mxCrc, double myCrc, double mcrc, bool converged,
        double? epsMaxTension, double? epsTensionLimit, double? utilization = null)
    {
        NText = $"{n:0.###}  кН";
        MxCrcText = $"{mxCrc:0.####}  кН·м";
        MyCrcText = $"{myCrc:0.####}  кН·м";
        McrcText = $"{mcrc:0.####}  кН·м";
        StatusText = converged ? Loc.S("ResultConvergedYes") : Loc.S("ResultConvergedNo");
        StatusBrush = converged ? Brushes.Green : Brushes.Red;

        HasEpsData = epsMaxTension.HasValue && epsTensionLimit.HasValue;
        if (HasEpsData)
        {
            EpsMaxTensionText = $"{epsMaxTension!.Value:+0.000000;-0.000000}";
            EpsTensionLimitText = $"{epsTensionLimit!.Value:+0.000000;-0.000000}";
        }

        HasUtilization = utilization.HasValue;
        if (HasUtilization)
            UtilizationText = $"{utilization!.Value:0.000}";
    }
}
