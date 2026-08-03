using System.Collections.ObjectModel;
using System.Text.Json;
using System.Windows.Media;
using CScore;
using OpenCS.OpenSees.Analysis;
using OpenCS.Tasks;
using OpenCS.Utilites;

namespace OpenCS.ViewModels;

/// <summary>Представляет историю одноосного OpenSees moment–curvature результата.</summary>
public sealed class OpenSeesSectionMomentCurvatureResultVM
{
    /// <summary>Итоговый статус расчёта.</summary>
    public string Status { get; }

    /// <summary>Локализованный текст итогового статуса.</summary>
    public string StatusText => Status switch
    {
        "ok" => Loc.S("OpenSeesMomentCurvatureStatusOk"),
        "not_converged" => Loc.S("OpenSeesMomentCurvatureStatusNotConverged"),
        _ => Loc.S("OpenSeesMomentCurvatureStatusError")
    };

    /// <summary>Цвет итогового статуса.</summary>
    public Brush StatusBrush => Status switch
    {
        "ok" => Brushes.SeaGreen,
        "not_converged" => Brushes.DarkOrange,
        _ => Brushes.Firebrick
    };

    /// <summary>История шагов moment–curvature.</summary>
    public ObservableCollection<OpenSeesSectionMomentCurvatureRowVM> Rows { get; } = [];

    /// <summary>Сошедшиеся строки, которые образуют физически завершённую часть графика.</summary>
    public IReadOnlyList<OpenSeesSectionMomentCurvatureRowVM> ConvergedRows =>
        Rows.Where(row => row.Converged).ToArray();

    /// <summary>Количество сошедшихся шагов.</summary>
    public int ConvergedRowCount => Rows.Count(row => row.Converged);

    /// <summary>Количество шагов в формате для сводной карточки.</summary>
    public string HistoryCountText => $"{ConvergedRowCount}/{Rows.Count}";

    /// <summary>Последний записанный шаг.</summary>
    public OpenSeesSectionMomentCurvatureRowVM? LastRow => Rows.LastOrDefault();

    /// <summary>Последний сошедшийся шаг.</summary>
    public OpenSeesSectionMomentCurvatureRowVM? LastConvergedRow =>
        Rows.LastOrDefault(row => row.Converged);

    /// <summary>Каталог артефактов запуска.</summary>
    public string ArtifactDirectory { get; }

    /// <summary>Диагностика внешнего процесса и разбора результата.</summary>
    public IReadOnlyList<string> Diagnostics { get; }

    /// <summary>Диагностика одной строкой для текстового блока.</summary>
    public string DiagnosticsText => Diagnostics.Count == 0
        ? Loc.S("OpenSeesMomentCurvatureNoDiagnostics")
        : string.Join(Environment.NewLine, Diagnostics);

    /// <summary>Создаёт модель отображения из сохранённого результата задачи.</summary>
    public OpenSeesSectionMomentCurvatureResultVM(CalcResult result)
    {
        ArgumentNullException.ThrowIfNull(result);
        Status = result.Status;

        SectionAnalysisResult? analysis = null;
        try
        {
            analysis = JsonSerializer.Deserialize<SectionAnalysisResult>(result.DataJson);
        }
        catch (JsonException)
        {
            // Подробность ошибки ниже извлекается из исходного DataJson.
        }

        if (analysis != null)
        {
            foreach (SectionHistoryRow row in analysis.Rows)
                Rows.Add(new OpenSeesSectionMomentCurvatureRowVM(row));
        }

        ArtifactDirectory = analysis?.ArtifactDirectory ?? "";
        List<string> diagnostics = analysis?.Diagnostics.ToList() ?? [];
        if (diagnostics.Count == 0)
        {
            string detail = CalcResultLogHelper.ExtractDetail(result);
            if (!string.IsNullOrWhiteSpace(detail))
                diagnostics.Add(detail);
        }

        Diagnostics = diagnostics;
    }
}

/// <summary>Одна строка истории moment–curvature в единицах интерфейса.</summary>
public sealed class OpenSeesSectionMomentCurvatureRowVM
{
    /// <summary>Номер шага.</summary>
    public int Step { get; }

    /// <summary>Коэффициент нагрузки OpenSees.</summary>
    public double LoadFactor { get; }

    /// <summary>Продольная сила в кН.</summary>
    public double AxialForceKn { get; }

    /// <summary>Момент в кН·м.</summary>
    public double MomentKnM { get; }

    /// <summary>Осевое перемещение/деформация.</summary>
    public double AxialStrain { get; }

    /// <summary>Кривизна в 1/м.</summary>
    public double Curvature { get; }

    /// <summary>Признак сходимости шага.</summary>
    public bool Converged { get; }

    /// <summary>Невязка шага.</summary>
    public double Residual { get; }

    /// <summary>Локализованный текст статуса шага.</summary>
    public string StatusText => Converged
        ? Loc.S("OpenSeesMomentCurvatureStepConverged")
        : Loc.S("OpenSeesMomentCurvatureStepNotConverged");

    /// <summary>Создаёт строку отображения из сырой строки OpenSees.</summary>
    public OpenSeesSectionMomentCurvatureRowVM(SectionHistoryRow row)
    {
        Step = row.Step;
        LoadFactor = row.LoadFactor;
        AxialForceKn = row.AxialForceN / 1000.0;
        MomentKnM = row.BendingMomentNm / 1000.0;
        AxialStrain = row.AxialStrain;
        Curvature = row.Curvature;
        Converged = row.Converged;
        Residual = row.Residual;
    }
}
