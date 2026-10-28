using CScore.Planar;
using CScore.Submodel;

namespace CScore.Fem.Fragment.Masks;

/// <summary>Вид ключа маски: загружение или сочетание стадии (секущий источник).</summary>
public enum FragmentMaskKeyKind { LoadCase, Stage }

/// <summary>
/// Ключ маски (спека разд. 3): загружение (<see cref="LoadCaseId"/>) или сочетание стадии — каноническое выражение
/// <see cref="StageExpressionJson"/>. Id — в нумерации того, кто ключ выдал (источник родителя или фрагмент).
/// </summary>
public sealed record FragmentMaskKey(FragmentMaskKeyKind Kind, int LoadCaseId, string? StageExpressionJson)
{
    public static FragmentMaskKey OfLoadCase(int loadCaseId) => new(FragmentMaskKeyKind.LoadCase, loadCaseId, null);
    public static FragmentMaskKey OfStage(string expressionJson) => new(FragmentMaskKeyKind.Stage, 0, expressionJson);

    /// <summary>
    /// Ключ сочетания в каноническом виде (равные сочетания — равные ключи): сумма слагаемых по возрастанию Id (повторы
    /// складываются), коэффициенты округлены до 1e−10, нулевые отброшены.
    /// </summary>
    public static FragmentMaskKey OfStageTerms(IEnumerable<(int LoadCaseId, double Factor)> terms)
    {
        var sum = new SortedDictionary<int, double>();
        foreach (var (id, k) in terms) sum[id] = sum.GetValueOrDefault(id) + k;
        var expression = new FemLoadExpression
        {
            Mode = FemLoadExpressionMode.Sum,
            Terms = sum.Select(kv => (kv.Key, K: Math.Round(kv.Value, 10))).Where(t => t.K != 0)
                .Select(t => new FemLoadTerm { LoadCaseId = t.Key, Coefficient = t.K }).ToList(),
        };
        return OfStage(expression.ToJson());
    }

    /// <summary>Слагаемые сочетания (Id, коэффициент); у ключа загружения — само загружение с 1.</summary>
    public IReadOnlyList<(int LoadCaseId, double Factor)> Terms() => Kind == FragmentMaskKeyKind.LoadCase
        ? [(LoadCaseId, 1.0)]
        : FemLoadExpression.Parse(StageExpressionJson).Terms.Select(t => (t.LoadCaseId, t.Coefficient)).ToList();

    /// <summary>Тот же ключ с Id загружений через <paramref name="map"/> (копия схемы, карта родителя → фрагмента).</summary>
    public FragmentMaskKey MapLoadCases(Func<int, int> map) => Kind == FragmentMaskKeyKind.LoadCase
        ? OfLoadCase(map(LoadCaseId))
        : OfStageTerms(Terms().Select(t => (map(t.LoadCaseId), t.Factor)));
}

/// <summary>Маска одного разреза фрагмента в БД (<c>fem_fragment_mask_items</c>): Id разреза, ключ, сэмплы.</summary>
public sealed record FragmentMaskItem(int CutId, FragmentMaskKey Key, IReadOnlyList<FragmentMaskSample> Samples)
{
    /// <summary>Хотя бы в одном сэмпле нет перемещений источника.</summary>
    public bool NoDisplacements => Samples.Count == 0 || Samples.Any(s => s.U == null);

    /// <summary>Хотя бы в одном сэмпле нет сил источника.</summary>
    public bool NoForces => Samples.Count == 0 || Samples.Any(s => s.F == null);
}

/// <summary>
/// Действующий набор масок фрагмента (спека разд. 3, 6; БД v94 <c>fem_fragment_masks</c>): источник, отпечаток
/// источника для сверки устаревания, отчёт снятия и маски по разрезам и ключам. У фрагмента один набор.
/// </summary>
public sealed class FragmentMaskSet
{
    /// <summary>Виды источника (<see cref="SourceKind"/>).</summary>
    public const string SourceCsfeaLinear = "csfea_linear", SourceCsfeaSecant = "csfea", SourceScad = "scad", SourceLira = "lira";

    public required string SourceKind { get; init; }
    /// <summary>Ссылка на источник: Id постановки родителя или вид вложения импорта.</summary>
    public string SourceRef { get; init; } = "";
    /// <summary>Подпись источника для пользователя (имя постановки).</summary>
    public string SourceLabel { get; init; } = "";
    /// <summary>Отпечаток источника: результат постановки и полоса родителя.</summary>
    public string SourceFingerprint { get; init; } = "";
    public string CreatedAt { get; init; } = "";
    /// <summary>Отчёт снятия — JSON диагностики (как <see cref="StoreyFragmentBuildReport"/>).</summary>
    public string ReportJson { get; init; } = "[]";
    public List<FragmentMaskItem> Items { get; init; } = [];
}

/// <summary>
/// Сэмпл маски в точке <see cref="S"/> вдоль сечения (м; у конца стержня 0), общие оси: <see cref="U"/> — перемещения
/// м и повороты рад; <see cref="F"/> — действие отброшенной части на фрагмент (кромка — Н/м и Н·м/м, конец стержня —
/// Н и Н·м), момент относительно точки сэмпла. null — данных нет в источнике.
/// </summary>
public sealed record FragmentMaskSample(double S, Dof6? U, Dof6? F);

/// <summary>Маска одного сечения рассечения (<see cref="CutIndex"/> — номер во входе экстрактора) по одному ключу.</summary>
public sealed record FragmentCutMask(int CutIndex, FragmentMaskKey Key, IReadOnlyList<FragmentMaskSample> Samples);

/// <summary>
/// Усилия КЭ пластины в центре: <c>Nx Ny Nxy</c> (Н/м), <c>Mx My Mxy</c> (Н·м/м), <c>Qx Qy</c> (Н/м) в осях
/// <see cref="AxisX"/>, <see cref="AxisY"/> (общие оси); конвенция OpenCS: <c>Mx = ∫σx·ζ dζ</c>, ζ — по
/// <c>AxisX × AxisY</c>.
/// </summary>
public sealed record FragmentShellForces(PlanarVector3 AxisX, PlanarVector3 AxisY, double[] Values)
{
    public const int Components = 8;
}

/// <summary>
/// Нагрузка на КЭ стержня родителя в общих осях: распределённая на участке долей длины [<see cref="A"/>, <see cref="B"/>]
/// от узла I (интенсивность линейно <see cref="QA"/> → <see cref="QB"/>, Н/м) или сосредоточенная в доле <see cref="A"/>
/// (<see cref="Force"/>, Н; <see cref="Moment"/>, Н·м).
/// </summary>
public sealed record FragmentBeamSpanLoad(bool IsPoint, double A, double B, PlanarVector3 QA, PlanarVector3 QB,
    PlanarVector3 Force, PlanarVector3 Moment)
{
    public static FragmentBeamSpanLoad Distributed(double a, double b, PlanarVector3 qa, PlanarVector3 qb) =>
        new(false, a, b, qa, qb, PlanarVector3.Zero, PlanarVector3.Zero);

    public static FragmentBeamSpanLoad Point(double x, PlanarVector3 force, PlanarVector3 moment) =>
        new(true, x, x, PlanarVector3.Zero, PlanarVector3.Zero, force, moment);
}

/// <summary>
/// Результат родителя для снятия масок (спека разд. 4.1), не зависящий от источника (CSfea, ЛИРА, SCAD …). Теги —
/// теги узлов и КЭ сетки родителя. Отсутствие данных — false, а не ноль.
/// </summary>
public interface IFragmentParentResult
{
    /// <summary>Ключи источника: загружения или стадии родителя.</summary>
    IReadOnlyList<FragmentMaskKey> Keys { get; }

    /// <summary>Перемещения узла сетки, общие оси.</summary>
    bool TryNodeDisplacement(FragmentMaskKey key, string meshNodeTag, out Dof6 displacement);

    /// <summary>Силы концов КЭ стержня, местные оси (<see cref="CScore.Fem.BeamLocalAxisConvention"/>), сила узла на КЭ.</summary>
    bool TryBeamEndForces(FragmentMaskKey key, string elemTag, out BeamEndForces forces);

    /// <summary>Усилия КЭ пластины в центре.</summary>
    bool TryShellForces(FragmentMaskKey key, string elemTag, out FragmentShellForces forces);

    /// <summary>Нагрузки КЭ стержня (для усилий внутри КЭ); пусто — нагрузок нет или они неизвестны.</summary>
    IReadOnlyList<FragmentBeamSpanLoad> BeamLoads(FragmentMaskKey key, string elemTag);

    /// <summary>Нагрузки КЭ стержней источнику известны (иначе усилия внутри КЭ — линейно между концами).</summary>
    bool KnowsBeamLoads { get; }
}
