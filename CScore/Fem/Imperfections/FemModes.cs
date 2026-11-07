using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace CScore.Fem.Imperfections;

/// <summary>Форма потери устойчивости и её амплитуда в элементе несовершенства «Формы».</summary>
public sealed record FemModeAmplitude
{
    /// <summary>Номер формы в задаче (с 1, по возрастанию λ).</summary>
    public int Mode { get; init; } = 1;
    /// <summary>Амплитуда, мм, со знаком: множитель формы, нормированной на наибольшее поступательное перемещение = 1.</summary>
    public double AmplitudeMm { get; init; }

    /// <summary>Источник амплитуды: <see cref="AmplitudeManual"/> (мм, <see cref="AmplitudeMm"/>) или <see cref="AmplitudeEn1993"/>.</summary>
    public string Amplitude { get; init; } = AmplitudeManual;

    /// <summary>Знак «−» амплитуды по (5.9) (у «мм» знак — в <see cref="AmplitudeMm"/>).</summary>
    public bool Minus { get; init; }

    /// <summary>Амплитуда вручную, мм.</summary>
    public const string AmplitudeManual = "mm";
    /// <summary>Амплитуда по EN 1993-1-1 п. 5.3.2(11), ф. (5.9)–(5.11).</summary>
    public const string AmplitudeEn1993 = "en1993_5_9";

    /// <summary>Амплитуда по (5.9).</summary>
    public bool IsEn1993 => Amplitude == AmplitudeEn1993;
}

/// <summary>
/// Параметры несовершенства по формам потери устойчивости (EN 1993-1-1 п. 5.3.2(11), спека 4.2, 6): постановка
/// «Расчётные длины — CSfea» (<c>csfea_buckling</c>), её задача (сочетание или огибающая сжатия) и формы с амплитудами;
/// u₀ = Σ aᵢ·φᵢ (сумма не перенормируется). В одном варианте расчёта не сочетаются с креном и погибями — «взамен».
/// </summary>
public sealed record FemModesParams
{
    /// <summary>Id постановки устойчивости схемы.</summary>
    public int AnalysisId { get; init; }
    /// <summary>Индекс задачи в сводке результата постановки (с 0).</summary>
    public int CaseIndex { get; init; }
    public List<FemModeAmplitude> Modes { get; init; } = [];

    /// <summary>γ_M1 для (5.10) (национальный параметр); null — рекомендуемое 1,0.</summary>
    public double? GammaM1 { get; init; }

    /// <summary>Кривая табл. 6.1 для (5.10): «a0»…«d»; null — по табл. 6.2 по сечению критического КЭ.</summary>
    public string? Curve { get; init; }

    /// <summary>Тег критического стержневого КЭ сетки для (5.9); null — по наименьшему α_ult,k.</summary>
    public string? CriticalElement { get; init; }

    public string ToJson() => JsonSerializer.Serialize(this, FemImperfectionJson.Options);
    public static FemModesParams Parse(string? json) => FemImperfectionJson.Parse<FemModesParams>(json);
}

/// <summary>
/// Форма потери устойчивости на сетке схемы (нейтрально, без кодека результата): номер узла сетки → 6 DOF (перемещения
/// и повороты, глобальные оси), номер стержневого КЭ → поступательные перемещения промежуточных точек дробления
/// (по 3 числа, от I к J, равномерно по длине); нормирована на наибольшее поступательное перемещение = 1.
/// </summary>
public sealed record FemModeShape(IReadOnlyDictionary<int, double[]> Nodes, IReadOnlyDictionary<int, double[][]> BarPoints)
{
    /// <summary>
    /// Изгибающие моменты формы EI·η″ (EN 1993-1-1 (5.9)): номер стержневого КЭ → (My, Mz) местных осей КЭ в узле I,
    /// точках дробления и узле J, Н·м на 1 м формы; null — запись без моментов (до подсреза 5б).
    /// </summary>
    public IReadOnlyDictionary<int, double[][]>? Moments { get; init; }
}

/// <summary>
/// Итог поиска форм для несовершенства (<see cref="FemImperfectionContext.Modes"/>): постановка и задача, λ форм задачи
/// и запрошенные формы; <see cref="ErrorCode"/> — почему форм нет (постановки или результата нет, формы устарели,
/// запись v1 и т. п.).
/// </summary>
public sealed record FemModesLookup
{
    public string? ErrorCode { get; init; }
    public string? Error { get; init; }
    public string AnalysisTag { get; init; } = "";
    public string CaseTag { get; init; } = "";
    /// <summary>λ форм задачи по возрастанию (λ₁ = α_cr задачи).</summary>
    public IReadOnlyList<double> Lambda { get; init; } = [];
    /// <summary>Номер формы (с 1) → форма; только запрошенные и найденные.</summary>
    public IReadOnlyDictionary<int, FemModeShape> Shapes { get; init; } = new Dictionary<int, FemModeShape>();
    /// <summary>
    /// Продольные силы стержневых КЭ задачи (номер КЭ → N, Н, растяжение — плюс), по которым получены λ — N_Ed для
    /// α_ult,k (5.11); null — не записаны (результат до подсреза 5б).
    /// </summary>
    public IReadOnlyDictionary<int, double>? Axial { get; init; }

    public static FemModesLookup Fail(string code, string message) => new() { ErrorCode = code, Error = message };
}

/// <summary>Форма в составе решённого несовершенства: номер, λ, амплитуда, м (со знаком и коэффициентом загружения).</summary>
public sealed record FemResolvedMode(int Mode, double Lambda, double AmplitudeM)
{
    /// <summary>Ход амплитуды по (5.9)–(5.11) для отчёта; пусто — вручную.</summary>
    public string Source { get; init; } = "";
}

/// <summary>
/// Несовершенство по формам (<see cref="FemModesParams"/>), привязанное к сетке: u₀ = Σ aᵢ·φᵢ уже просуммировано —
/// <paramref name="Nodes"/> (узел сетки → 6 чисел, м и рад), <paramref name="BarPoints"/> (стержневой КЭ → точки
/// дробления, м); <paramref name="Modes"/> — состав для отчёта.
/// </summary>
public sealed record FemResolvedModes(string Label, string AnalysisTag, string CaseTag, IReadOnlyList<FemResolvedMode> Modes,
    IReadOnlyDictionary<int, double[]> Nodes, IReadOnlyDictionary<int, double[][]> BarPoints) : FemResolvedImperfection(Label)
{
    /// <summary>λ₁ задачи постановки — α_cr для отчёта (п. 5.2.1(3)); null — не известен.</summary>
    public double? AlphaCr { get; init; }

    /// <summary>Наибольшее поступательное |u₀| суммы форм, м.</summary>
    public double MaxU0 => Nodes.Values.Select(u => Norm(u[0], u[1], u[2]))
        .Concat(BarPoints.Values.SelectMany(p => p).Select(u => Norm(u[0], u[1], u[2]))).DefaultIfEmpty(0).Max();

    static double Norm(double x, double y, double z) => Math.Sqrt(x * x + y * y + z * z);
}

/// <summary>
/// Отпечаток геометрии схемы (спека 6): узлы сетки (теги, координаты), КЭ (тег, вид, узлы, поворот, шарниры, сечение,
/// жёсткость, материал, толщина, постель, кручение), закрепления, пружины, жёсткие тела — без нагрузок, сочетаний и
/// осей выдачи усилий. Формы постановки устойчивости годятся для несовершенства, пока отпечаток при их расчёте совпадает
/// с текущим. Порядок записей не влияет (сортировка по тегам). Значения жёсткостей из таблиц (номер жёсткости) — по
/// номеру, правка таблицы жёсткостей отпечаток не меняет.
/// </summary>
public static class FemGeometryFingerprint
{
    public static string Compute(IReadOnlyList<FemMeshNode> meshNodes, IReadOnlyList<FemElement> meshElements,
        IReadOnlyList<FemMeshNodeSupport> supports, IReadOnlyList<FemSpring> springs, IReadOnlyList<FemRigidBody> rigidBodies)
    {
        ArgumentNullException.ThrowIfNull(meshNodes);
        ArgumentNullException.ThrowIfNull(meshElements);
        var ci = CultureInfo.InvariantCulture;
        var sb = new StringBuilder();
        string N(double v) => v.ToString("R", ci);
        string O(double? v) => v is { } x ? N(x) : "-";
        string I(int? v) => v?.ToString(ci) ?? "-";
        foreach (var n in meshNodes.OrderBy(n => n.NodeTag, StringComparer.Ordinal))
            sb.Append('n').Append(n.NodeTag).Append(':').Append(N(n.X)).Append(',').Append(N(n.Y)).Append(',').Append(N(n.Z)).Append('\n');
        foreach (var e in meshElements.OrderBy(e => e.ElemTag, StringComparer.Ordinal))
            sb.Append('e').Append(e.ElemTag).Append(':').Append(e.ElemType).Append('|').Append(e.NodeIdsJson).Append('|')
                .Append(O(e.BeamRotationDeg)).Append('|').Append(I(e.ReleaseI)).Append(',').Append(I(e.ReleaseJ)).Append('|')
                .Append(e.SectionTag).Append('|').Append(I(e.StiffnessNum)).Append('|').Append(I(e.CrossSectionId)).Append('|')
                .Append(e.MaterialTag).Append('|').Append(O(e.ThicknessM)).Append('|').Append(O(e.FoundationC1)).Append('|')
                .Append(e.SourceMemberTag).Append('|').Append(e.GjStrategy).Append(',').Append(O(e.GjManualValue)).Append(',')
                .Append(I(e.GjTorsionTaskId)).Append('\n');
        foreach (var s in (supports ?? []).OrderBy(s => s.NodeTag, StringComparer.Ordinal).ThenBy(s => s.Mask))
            sb.Append('u').Append(s.NodeTag).Append(':').Append(s.Mask.ToString(ci)).Append('\n');
        foreach (var s in (springs ?? []).OrderBy(s => s.NodeTag, StringComparer.Ordinal).ThenBy(s => s.TargetKind, StringComparer.Ordinal))
            sb.Append('k').Append(s.TargetKind).Append(':').Append(s.NodeTag).Append(':').Append(string.Join(',',
                new[] { s.Kx, s.Ky, s.Kz, s.Kux, s.Kuy, s.Kuz }.Select(N))).Append('\n');
        foreach (var r in (rigidBodies ?? []).OrderBy(r => r.MasterNodeTag, StringComparer.Ordinal)
                     .ThenBy(r => r.SlaveNodeTagsJson, StringComparer.Ordinal))
            sb.Append('r').Append(r.MasterNodeTag).Append(':').Append(string.Join(',', r.SlaveNodeTags)).Append('|')
                .Append(r.Mask.ToString(ci)).Append('|').Append(r.Kind).Append('\n');
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(sb.ToString())));
    }
}
