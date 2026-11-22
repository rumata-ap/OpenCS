using CScore;
using CSmath;
using OpenCS.Utilites;

namespace OpenCS.Services;

/// <summary>Точка σ(ε) пользовательской диаграммы в единицах базы: ε безразмерная, σ в кПа (сжатие — отрицательные).</summary>
public readonly record struct DiagramPointData(double Eps, double Sig);

/// <summary>
/// Создание, правка и удаление диаграмм пула проекта: одна проверка и одно сохранение для страницы диаграммы,
/// построения диаграммы из набора характеристик и консоли (<c>opencs apply</c>).
/// </summary>
public static class DiagramAuthoring
{
    const double Zero = 1e-15;

    /// <summary>Типы материала, для которых диаграмма пула имеет смысл (базовый тип custom-материала).</summary>
    public static readonly MatType[] MaterialTypes = [MatType.Concrete, MatType.ReSteelF, MatType.ReSteelU, MatType.Steel];

    /// <summary>
    /// Точки обеих ветвей по возрастанию ε; начало координат — одной точкой. Обратное к <see cref="Apply"/>.
    /// </summary>
    public static List<DiagramPointData> ToPoints(Diagramm d)
    {
        var list = new List<DiagramPointData>();
        bool seenZero = false;
        void AddBranch(ISpline? sp)
        {
            if (sp?.X == null) return;
            for (int i = 0; i < sp.X.Length; i++)
            {
                bool isZero = Math.Abs(sp.X[i]) < Zero && Math.Abs(sp.Y[i]) < Zero;
                if (isZero) { if (seenZero) continue; seenZero = true; }
                list.Add(new DiagramPointData(sp.X[i], sp.Y[i]));
            }
        }
        AddBranch(d.Ic);
        AddBranch(d.It);
        return list.OrderBy(p => p.Eps).ToList();
    }

    /// <summary>
    /// Проверяет точки, введённые пользователем (таблица страницы, документ консоли): конечные числа, без повтора ε,
    /// при ε = 0 σ = 0, по две точки на каждой ветви (начало координат добавляется само и входит в обе).
    /// Пути ошибок — <c>{path}[i]</c>, как в документе opencs-model.
    /// </summary>
    public static IReadOnlyList<FieldError> ValidatePoints(IReadOnlyList<DiagramPointData> points, string path = "points")
    {
        var errors = new List<FieldError>();
        for (int i = 0; i < points.Count; i++)
        {
            var p = points[i];
            if (!double.IsFinite(p.Eps) || !double.IsFinite(p.Sig))
                errors.Add(new($"{path}[{i}]", Loc.S("DiagramErrNotFinite")));
            else if (Math.Abs(p.Eps) < Zero && Math.Abs(p.Sig) > 1e-9)
                errors.Add(new($"{path}[{i}]", Loc.S("DiagramErrOriginStress")));
        }
        if (errors.Count > 0) return errors;

        var byEps = points.Select((p, i) => (p, i)).OrderBy(t => t.p.Eps).ToList();
        for (int k = 1; k < byEps.Count; k++)
            if (Math.Abs(byEps[k].p.Eps - byEps[k - 1].p.Eps) < Zero)
                errors.Add(new($"{path}[{byEps[k].i}]",
                    string.Format(Loc.S("DiagramErrDuplicateEps"), Num(byEps[k].p.Eps), byEps[k - 1].i)));

        if (!points.Any(p => p.Eps < -Zero)) errors.Add(new(path, Loc.S("DiagramErrNoCompression")));
        if (!points.Any(p => p.Eps > Zero)) errors.Add(new(path, Loc.S("DiagramErrNoTension")));
        return errors;
    }

    /// <summary>
    /// Строит ветви Ic (ε ≤ 0) и It (ε ≥ 0) диаграммы из точек (σ в кПа) и записывает характерные деформации.
    /// Точки должны пройти <see cref="ValidatePoints"/>.
    /// </summary>
    public static void Apply(Diagramm target, IEnumerable<DiagramPointData> points, IEnumerable<double> characteristic)
    {
        var sorted = points.OrderBy(p => p.Eps).ToList();
        if (!sorted.Any(p => Math.Abs(p.Eps) < Zero))
        {
            int idx = sorted.FindIndex(p => p.Eps > 0);
            sorted.Insert(idx < 0 ? sorted.Count : idx, new DiagramPointData(0, 0));
        }
        var ic = sorted.Where(p => p.Eps <= Zero).ToList();
        var it = sorted.Where(p => p.Eps >= -Zero).ToList();
        target.Ic = new LSpline(ic.Select(p => p.Eps).ToArray(), ic.Select(p => p.Sig).ToArray());
        target.It = new LSpline(it.Select(p => p.Eps).ToArray(), it.Select(p => p.Sig).ToArray());
        target.CharacteristicStrains = characteristic.OrderBy(e => e).ToList();
    }

    /// <summary>Проверка собранной диаграммы перед записью: обозначение, тип материала, ветви, характерные точки.</summary>
    public static IReadOnlyList<FieldError> Validate(Diagramm d)
    {
        var errors = new List<FieldError>();
        if (string.IsNullOrWhiteSpace(d.Tag)) errors.Add(new("tag", Loc.S("DiagramErrTagEmpty")));
        if (!MaterialTypes.Contains(d.MaterialType)) errors.Add(new("materialType", Loc.S("DiagramErrMaterialType")));
        if (!Enum.IsDefined(d.CalcType)) errors.Add(new("calcType", Loc.S("DiagramErrCalcType")));
        if (!BranchOk(d.Ic, compression: true)) errors.Add(new("points", Loc.S("DiagramErrNoCompression")));
        if (!BranchOk(d.It, compression: false)) errors.Add(new("points", Loc.S("DiagramErrNoTension")));
        if (errors.Count > 0) return errors;

        double lo = d.Ic.X[0], hi = d.It.X[^1];
        var cs = d.CharacteristicStrains ?? [];
        for (int i = 0; i < cs.Count; i++)
            if (!double.IsFinite(cs[i]) || cs[i] < lo - Zero || cs[i] > hi + Zero)
                errors.Add(new($"characteristic[{i}]", string.Format(Loc.S("DiagramErrCharacteristicRange"), Num(lo), Num(hi))));
        return errors;
    }

    /// <summary>Ветвь: ≥ 2 точек, ε строго возрастает, конец (сжатие) или начало (растяжение) — в начале координат.</summary>
    static bool BranchOk(ISpline? sp, bool compression)
    {
        if (sp?.X == null || sp.Y == null || sp.X.Length < 2 || sp.X.Length != sp.Y.Length) return false;
        for (int i = 0; i < sp.X.Length; i++)
            if (!double.IsFinite(sp.X[i]) || !double.IsFinite(sp.Y[i])) return false;
        for (int i = 1; i < sp.X.Length; i++)
            if (!(sp.X[i] > sp.X[i - 1])) return false;
        int o = compression ? sp.X.Length - 1 : 0;
        return Math.Abs(sp.X[o]) < Zero && Math.Abs(sp.Y[o]) < 1e-9;
    }

    /// <summary>Проверяет и сохраняет (новая — добавляется в пул <see cref="DatabaseService.Diagrams"/>). Ошибки — ничего не записано.</summary>
    public static IReadOnlyList<FieldError> Save(DatabaseService db, Diagramm d)
    {
        var errors = Validate(d);
        if (errors.Count > 0) return errors;
        db.SaveDiagram(d);
        if (!db.Diagrams.Contains(d)) db.Diagrams.Add(d);
        return [];
    }

    /// <summary>Обозначения материалов custom, ссылающихся на диаграмму пула.</summary>
    public static List<string> UsedBy(IEnumerable<Material> materials, int diagramId) =>
        diagramId <= 0
            ? []
            : materials.Where(m => m.Type == MatType.Custom && m.CustomDiagramIds.Values.Contains(diagramId))
                       .Select(m => m.Tag).ToList();

    /// <summary>
    /// Удаляет диаграмму из базы и пула, если ни один материал custom на неё не ссылается; иначе — false и
    /// обозначения материалов в <paramref name="usedBy"/>.
    /// </summary>
    public static bool TryDelete(DatabaseService db, Diagramm d, out List<string> usedBy)
    {
        usedBy = UsedBy(db.Materials, d.Id);
        if (usedBy.Count > 0) return false;
        if (d.Id > 0) db.DeleteDiagram(d);
        db.Diagrams.Remove(d);
        return true;
    }

    static string Num(double v) => v.ToString("G6", System.Globalization.CultureInfo.InvariantCulture);
}
