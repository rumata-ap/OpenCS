using CScore.Fem;
using CScore.Fem.Editing;
using OpenCS.Utilites;

namespace OpenCS.Services;

/// <summary>
/// Граничные условия схемы МКЭ — правила одни для окон («Закрепления и пружины…», «Линейная связь»), «Сохранить»
/// схемы и вида <c>femSchemas</c> документа <c>opencs-model</c>: жёсткость — конечное неотрицательное число, пружина по
/// закреплённому направлению не задаётся. Направления — общие оси X Y Z UX UY UZ (биты 0–5 маски).
/// </summary>
public static class FemBoundaryAuthoring
{
    /// <summary>Обозначения направлений (биты 0–5) — как в окнах и отчётах.</summary>
    public static readonly string[] DofNames = ["X", "Y", "Z", "UX", "UY", "UZ"];

    /// <summary>Обозначения жёсткостей по направлениям.</summary>
    public static readonly string[] SpringNames = ["Kx", "Ky", "Kz", "Kux", "Kuy", "Kuz"];

    /// <summary>Ошибка жёсткости по направлению: null — верно.</summary>
    /// <param name="isFixed">Направление закреплено.</param>
    public static string? StiffnessError(bool isFixed, double k)
    {
        if (!double.IsFinite(k) || k < 0) return Loc.S("FieldErrNonNegative");
        if (isFixed && k != 0) return Loc.S("FemBoundaryErrSpringFixed");
        return null;
    }

    /// <summary>Ошибки набора: (направление, текст) по всем шести DOF.</summary>
    public static List<(int Dof, string Message)> Check(int mask, IReadOnlyList<double> k)
    {
        var errors = new List<(int, string)>();
        for (int i = 0; i < 6; i++)
            if (StiffnessError((mask & (1 << i)) != 0, k[i]) is { } e) errors.Add((i, e));
        return errors;
    }

    /// <summary>
    /// Общие значения набора узлов или граней для окна: закрепление и жёсткость по направлению — общие у всех или
    /// null («разные»).
    /// </summary>
    public static (bool?[] Fix, double?[] K) Common(IReadOnlyCollection<(int Mask, IReadOnlyList<double> K)> items)
    {
        var fix = new bool?[6];
        var k = new double?[6];
        for (int i = 0; i < 6; i++)
        {
            int bit = 1 << i, dof = i;
            var masks = items.Select(x => (x.Mask & bit) != 0).Distinct().ToList();
            var values = items.Select(x => x.K[dof]).Distinct().ToList();
            fix[i] = masks.Count == 1 ? masks[0] : items.Count == 0 ? false : null;
            k[i] = values.Count == 1 ? values[0] : items.Count == 0 ? 0 : null;
        }
        return (fix, k);
    }

    /// <summary>
    /// Значения окна поверх прежних у одного объекта: null («разные», поле не тронуто) — прежнее значение. По
    /// закреплённому направлению жёсткость обнуляется (в окне поле закреплённого направления недоступно).
    /// </summary>
    public static (int Mask, double[] K) Apply(int oldMask, IReadOnlyList<double> oldK, IReadOnlyList<bool?> fix,
        IReadOnlyList<double?> k)
    {
        int mask = 0;
        var result = new double[6];
        for (int i = 0; i < 6; i++)
        {
            bool isFixed = fix[i] ?? (oldMask & (1 << i)) != 0;
            if (isFixed) mask |= 1 << i;
            result[i] = isFixed ? 0 : k[i] ?? oldK[i];
        }
        return (mask, result);
    }

    /// <summary>Жёсткости пружин узла КонЭ в сессии, Н/м и Н·м/рад (сумма записей узла).</summary>
    public static double[] NodeStiffnesses(FemSchemaEditSession session, string nodeTag)
    {
        var k = new double[6];
        foreach (var spring in session.NodeSprings.Where(s => s.NodeTag == nodeTag))
            for (int i = 0; i < 6; i++) k[i] += spring.Stiffnesses[i];
        return k;
    }

    /// <summary>
    /// Проверка ГУ сессии перед записью: пружины узлов КонЭ и линейные связи граней (жёсткость ≥ 0, пружина не по
    /// закреплённому направлению). Ошибки — с тегом узла или КонЭ в <see cref="FemValidationDiagnostic.SourceKeys"/>.
    /// </summary>
    public static List<FemValidationDiagnostic> Validate(FemSchemaEditSession session)
    {
        var result = new List<FemValidationDiagnostic>();
        var masks = session.Nodes.GroupBy(n => n.NodeTag).ToDictionary(g => g.Key, g => g.First().DofMask);
        foreach (var tag in session.NodeSprings.Select(s => s.NodeTag).Distinct())
        {
            if (!masks.TryGetValue(tag, out int mask)) continue;  // пружины удалённых узлов не пишутся
            foreach (var (dof, message) in Check(mask, NodeStiffnesses(session, tag)))
                result.Add(new FemValidationDiagnostic("node_spring_invalid",
                    string.Format(Loc.S("FemBoundaryErrNode"), tag, DofNames[dof], message), true, [tag]));
        }
        foreach (var s in session.LineSupports)
            foreach (var (dof, message) in Check(s.Mask, s.Stiffnesses))
                result.Add(new FemValidationDiagnostic("line_support_invalid",
                    string.Format(Loc.S("FemBoundaryErrLine"), s.Key, DofNames[dof], message), true, [s.MemberTag]));
        return result;
    }
}
