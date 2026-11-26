using System.Globalization;
using CScore.Fem;
using OpenCS.Utilites;

namespace OpenCS.ViewModels;

/// <summary>Строка таблицы узлов сетки: узел и его ГУ сеточного уровня (закрепление, пружина, происхождение).</summary>
public sealed class FemMeshNodeRow
{
    public FemMeshNodeRow(FemMeshNode node) => Node = node;

    public FemMeshNode Node { get; }
    public string NodeTag => Node.NodeTag;
    public double X => Node.X;
    public double Y => Node.Y;
    public double Z => Node.Z;
    public string? SourceNodeTag => Node.SourceNodeTag;
    public string? SourceMemberTag => Node.SourceMemberTag;

    /// <summary>Маска закреплённых DOF (биты 0–5: X Y Z UX UY UZ).</summary>
    public int SupportMask { get; private set; }

    /// <summary>Жёсткости пружины X Y Z UX UY UZ, Н/м и Н·м/рад (нули — пружины нет).</summary>
    public double[] Stiffnesses { get; private set; } = new double[6];

    /// <summary>Происхождения записей ГУ узла («manual», «import:scad»…).</summary>
    public IReadOnlyList<string> Origins { get; private set; } = [];

    public bool HasBoundary => SupportMask != 0 || Stiffnesses.Any(k => k != 0);

    public string SupportText => SupportMask == 0 ? "" : FemBoundaryDofs.Describe(SupportMask);

    /// <summary>Пружины в кН/м и кН·м/рад: «Z 1 250; UZ 40».</summary>
    public string SpringText => DescribeSprings(Stiffnesses);

    /// <summary>Жёсткости (Н/м, Н·м/рад) в кН: «Z 1 250; UZ 40»; нет — пустая строка.</summary>
    public static string DescribeSprings(IReadOnlyList<double> k)
    {
        string[] names = ["X", "Y", "Z", "UX", "UY", "UZ"];
        return string.Join("; ", Enumerable.Range(0, 6).Where(i => k[i] != 0)
            .Select(i => $"{names[i]} {(k[i] / 1e3).ToString("#,0.###", CultureInfo.CurrentCulture)}"));
    }

    /// <summary>
    /// ГУ, которые узел получает при расчёте от конструктивного уровня: закрепления и пружины узла КонЭ, линейные связи
    /// граней («X Y Z — узел КонЭ 1»; «Z; пружины Z 5 — П1, сторона 2»). Правка ГУ узла сетки их не меняет.
    /// </summary>
    public string InheritedText { get; private set; } = "";

    /// <summary>Добавляет к <see cref="InheritedText"/> ГУ одного источника.</summary>
    public void AddInherited(int mask, IReadOnlyList<double> k, string source)
    {
        var parts = new List<string>();
        if ((mask & FemBoundaryDofs.All) != 0) parts.Add(FemBoundaryDofs.Describe(mask & FemBoundaryDofs.All));
        if (DescribeSprings(k) is { Length: > 0 } springs) parts.Add(string.Format(Loc.S("FemMeshNodeInheritedSprings"), springs));
        if (parts.Count == 0) return;
        string text = $"{string.Join("; ", parts)} — {source}";
        InheritedText = InheritedText.Length == 0 ? text : $"{InheritedText}  |  {text}";
    }

    /// <summary>Источники жёсткостей поверх записей узла: «поле «Rz свай»», «вручную» (правка величины).</summary>
    public IReadOnlyList<string> ValueSources { get; private set; } = [];

    public string OriginText => string.Join(", ", Origins.Select(DescribeOrigin).Concat(ValueSources));

    /// <summary>Подпись происхождения: «вручную», «SCAD», «ЛИРА», «авто» (АЖТ примыканий).</summary>
    public static string DescribeOrigin(string origin) => origin switch
    {
        _ when origin.StartsWith(CScore.Fem.Fragment.StoreyFragmentBuilder.ParentRigidOriginPrefix, StringComparison.Ordinal) =>
            $"{Loc.S("FemBoundaryOriginParent")}: {DescribeOrigin(CScore.Fem.Fragment.StoreyFragmentBuilder.BaseOrigin(origin))}",
        FemLoadOrigin.Manual => Loc.S("FemBoundaryOriginManual"),
        "import:scad" => "SCAD",
        "import:lira" => Loc.S("FemBoundaryOriginLira"),
        CScore.Fem.RigidJunctionTraceSet.Provenance => Loc.S("FemBoundaryOriginAuto"),
        CScore.Fem.FemNodeRigidBodies.Provenance => Loc.S("FemBoundaryOriginNodePoints"),
        CScore.Fem.Fields.FemFieldOverlay.FieldOrigin => Loc.S("FemFieldOriginField"),
        _ => origin,
    };

    /// <summary>
    /// Задаёт ГУ строки по записям узла (пружины — действующие, с учётом полей); <paramref name="valueSources"/> —
    /// подписи полей и правок, задавших жёсткости. Пружина, созданная полем, своего происхождения не показывает —
    /// его называет подпись поля.
    /// </summary>
    public void Apply(IEnumerable<FemMeshNodeSupport> supports, IEnumerable<FemSpring> springs,
        IEnumerable<string>? valueSources = null)
    {
        var s = supports.ToList();
        var k = new double[6];
        var origins = s.Select(x => x.Origin).ToList();
        foreach (var spring in springs)
        {
            var add = spring.Stiffnesses;
            for (int i = 0; i < 6; i++) k[i] += add[i];
            if (spring.Origin != CScore.Fem.Fields.FemFieldOverlay.FieldOrigin) origins.Add(spring.Origin);
        }
        SupportMask = s.Aggregate(0, (m, x) => m | (x.Mask & FemBoundaryDofs.All));
        Stiffnesses = k;
        Origins = origins.Distinct(StringComparer.Ordinal).ToList();
        ValueSources = valueSources?.Distinct(StringComparer.Ordinal).ToList() ?? [];
    }

    /// <summary>ГУ после ручной правки.</summary>
    public void SetManual(int mask, IReadOnlyList<double> stiffnesses)
    {
        SupportMask = mask & FemBoundaryDofs.All;
        Stiffnesses = [.. stiffnesses];
        Origins = HasBoundary ? [FemLoadOrigin.Manual] : [];
        ValueSources = [];
    }
}
