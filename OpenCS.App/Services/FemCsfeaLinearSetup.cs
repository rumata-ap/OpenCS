using CSfea.CScoreBridge.Structural;
using OpenCS.Tasks;
using OpenCS.Utilites;

namespace OpenCS.Services;

/// <summary>
/// Сечения и замороженное состояние линейного расчёта CSfea (спека 2026-11-05, разд. 5.2): упругие сечения или
/// записанные секущие матрицы источника жёсткостей (<see cref="RecordedRcSectionFactory"/>), K_σ по перемещениям
/// источника при P-Δ. Общие для раннера и узловых сил масок — f_int КЭ обязаны повторять матрицу системы.
/// </summary>
public sealed record FemCsfeaLinearSetup(FemCsfeaStiffnessSourceData? Source, RecordedRcSectionFactory? Sections,
    RcSecantStepFields? GeometricFields, bool ShellInPlanePDelta, double? StiffnessFloor)
{
    /// <summary>Упругие сечения, первый порядок.</summary>
    public static FemCsfeaLinearSetup Elastic { get; } = new(null, null, null, true, null);

    /// <summary>P-Δ источника: K_σ по его перемещениям.</summary>
    public bool PDelta => GeometricFields != null;

    /// <summary>
    /// Настройка по параметрам постановки; <paramref name="floor"/> — нижний предел e_min (0 &lt; e_min &lt; 1, иначе —
    /// по умолчанию). null — источник жёсткостей пропал или его сетка не совпадает с моделью (<paramref name="error"/>).
    /// Каждый вызов создаёт новую фабрику — у неё своё состояние (КЭ на нижнем пределе).
    /// </summary>
    public static FemCsfeaLinearSetup? Resolve(DatabaseService db, int schemaId, FemCsfeaParams csfea, RcStructuralModel model,
        double floor, out string? error)
    {
        ArgumentNullException.ThrowIfNull(csfea);
        error = null;
        if (csfea.StiffnessSource is not { } stiffness) return Elastic;
        var source = FemCsfeaStiffnessSources.Load(db, schemaId, stiffness, out var sourceError);
        error = sourceError ?? (source != null ? FemCsfeaRunner.MeshMismatch(model, source) : "Источник жёсткостей не найден.");
        if (error != null || source == null) return null;
        double f = floor is > 0 and < 1 ? floor : CalcSettings.DefaultCsfeaStiffnessFloor;
        bool pDelta = csfea.SourcePDelta && source.GeomNonlinear;
        return new(source, new RecordedRcSectionFactory(source.Fields, f), pDelta ? source.Fields : null, source.ShellInPlanePDelta, f);
    }
}
