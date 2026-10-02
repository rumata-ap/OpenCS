using System.Globalization;
using CScore.Fem;

namespace CScore.Import;

/// <summary>
/// Общие данные источников сечения стержня по данным SCAD: размеры — из жёсткости КЭ (брус S0),
/// материалы — по классам ЖБ-группы КЭ среди материалов проекта, а у КЭ вне ЖБ-групп — материалы
/// сечения, назначенного ему в проекте. Привязки арматуры a1/a2 — из ЖБ-группы.
/// </summary>
/// <param name="stiffnesses">Жёсткости схемы по номеру.</param>
/// <param name="selected">Выгрузка подбора плагина SCAD; null — не приложена.</param>
/// <param name="groups">ЖБ-группы схемы; null — не прочитаны.</param>
/// <param name="concreteByClass">Бетон проекта по классу («B25»); null — такого нет.</param>
/// <param name="rebarByClass">Арматура проекта по классу («A500»); null — такой нет.</param>
/// <param name="projectSection">Сечение, назначенное КЭ в проекте (своё, элемента или цели).</param>
public sealed class ScadBarSectionContext(
    IReadOnlyDictionary<int, LiraStiffnessRecord> stiffnesses,
    ScadSelectedRebarFile? selected,
    ScadConcreteGroupIndex? groups,
    Func<string, Material?> concreteByClass,
    Func<string, Material?> rebarByClass,
    Func<FemCheckScopeElement, CrossSection?>? projectSection = null)
{
    readonly Dictionary<(int Stiffness, string Concrete, string Rebar), (LiraBarProfile? Profile, string? Reason)> _cache = [];

    /// <summary>Выгрузка подбора SCAD.</summary>
    public ScadSelectedRebarFile? Selected => selected;

    /// <summary>Подбор стержня в выгрузке; null — выгрузки нет или КЭ в ней нет.</summary>
    public ScadSelectedBar? SelectedBar(FemCheckScopeElement element) =>
        selected != null && element.ElemNum is int num && selected.Bars.TryGetValue(num, out var bar) ? bar : null;

    /// <summary>ЖБ-группа КЭ; null — групп нет или КЭ ни в одной.</summary>
    public ScadConcreteGroup? Group(FemCheckScopeElement element) =>
        element.ElemNum is int num ? groups?.Find(num) : null;

    /// <summary>Привязки арматуры a1 (низ) и a2 (верх) КЭ, м, либо причина, по которой их нет.</summary>
    public ((double A1, double A2)? Covers, string? Reason) Covers(FemCheckScopeElement element)
    {
        if (Group(element) is not { } g)
            return (null, groups == null
                ? "ЖБ-группы SCAD у схемы не прочитаны — привязки арматуры неизвестны"
                : "КЭ нет в ЖБ-группах SCAD — привязки арматуры неизвестны");
        return (ScadConcreteGroupIndex.BarCovers(g), null);
    }

    /// <summary>Размеры и материалы сечения КЭ либо причина, по которой их нет.</summary>
    public (LiraBarProfile? Profile, string? Reason) Profile(FemCheckScopeElement element)
    {
        if (element.Element.StiffnessNum is not int num)
            return (null, "у КЭ нет номера жёсткости");

        if (Group(element) is { } g && g.ConcreteClass.Length > 0 && g.LongitudinalRebarClass.Length > 0)
        {
            var key = (num, g.ConcreteClass, g.LongitudinalRebarClass);
            if (!_cache.TryGetValue(key, out var cached))
                _cache[key] = cached = Build(num, g);
            return cached;
        }

        // КЭ вне ЖБ-групп: материалы сечения, назначенного ему в проекте.
        var (rect, shapeReason) = Shape(num);
        if (rect == null) return (null, shapeReason);
        var own = projectSection?.Invoke(element);
        var concrete = own?.Areas.FirstOrDefault(a => a.Category == AreaCategory.Region && a.Material?.Type == MatType.Concrete)?.Material;
        var rebar = own?.Areas.FirstOrDefault(a => a.Category == AreaCategory.RebarGroup && a.Material != null)?.Material;
        if (concrete == null || rebar == null)
            return (null, "классы материалов неизвестны: КЭ нет в ЖБ-группах SCAD, а у цели нет сечения с бетоном и арматурой");
        return (new LiraBarProfile(num, rect.WidthM, rect.HeightM, concrete, rebar), null);
    }

    (LiraBarRect? Rect, string? Reason) Shape(int num)
    {
        if (!stiffnesses.TryGetValue(num, out var stiffness))
            return (null, $"жёсткости {num} нет среди жёсткостей схемы");
        return ScadStiffnessParams.BarRect(stiffness) is { } rect
            ? (rect, null)
            : (null, $"жёсткость {num} «{stiffness.Name}»: форма сечения не поддерживается (только брус S0)");
    }

    (LiraBarProfile?, string?) Build(int num, ScadConcreteGroup g)
    {
        var (rect, shapeReason) = Shape(num);
        if (rect == null) return (null, shapeReason);
        if (concreteByClass(g.ConcreteClass) is not { } concrete)
            return (null, $"в проекте нет бетона {g.ConcreteClass} (ЖБ-группа SCAD {g.Num})");
        if (rebarByClass(g.LongitudinalRebarClass) is not { } rebar)
            return (null, $"в проекте нет арматуры {g.LongitudinalRebarClass} (ЖБ-группа SCAD {g.Num})");
        return (new LiraBarProfile(num, rect.WidthM, rect.HeightM, concrete, rebar), null);
    }
}

/// <summary>
/// Раскладка подобранной SCAD арматуры стержня (средняя схема подсказки окна «Подбор арматуры
/// железобетонного сечения», прямоугольник). Оси — как у <see cref="LiraBarSectionBuilder"/>:
/// x ‖ Y1, y ‖ Z1. S1 — грань −Z1, S2 — +Z1 (обе с угловыми), S3 — грань −Y1, S4 — +Y1;
/// каждая S — вся площадь грани.
/// </summary>
public static class ScadBarSectionBuilder
{
    /// <summary>
    /// Точки арматуры: S1 — <see cref="LiraBarSectionBuilder.DistributedPoints"/> точек на y = −H/2 + a1
    /// от x = −B/2 + a до B/2 − a включая концы (a = min(a1, a2)), S2 — то же на y = H/2 − a2;
    /// S3/S4 — столько же точек на x = −B/2 + a и B/2 − a строго между рядами S1 и S2 (углы не задваиваются).
    /// Площадь грани делится поровну, диаметр точки — эквивалентный по площади.
    /// </summary>
    /// <param name="s">Подбор в сечении.</param>
    /// <param name="profile">Размеры и материалы.</param>
    /// <param name="a1">Привязка низа, м.</param>
    /// <param name="a2">Привязка верха, м.</param>
    /// <returns>Стержни либо причина, по которой раскладка невозможна.</returns>
    public static (List<LiraBarPoint>? Bars, string? Reason) SelectedLayout(
        ScadSelectedBarSection s, LiraBarProfile profile, double a1, double a2)
    {
        if (!s.IsComplete)
            return (null, "подбор SCAD в сечении не выполнен");

        double b = profile.WidthM, h = profile.HeightM, a = Math.Min(a1, a2);
        if (!(a1 > 0 && a2 > 0) || a1 + a2 >= h || 2 * a >= b)
            return (null, string.Format(CultureInfo.InvariantCulture,
                "привязки арматуры {0:0.#}/{1:0.#} мм не помещаются в сечение {2:0.#}×{3:0.#} мм",
                a1 * 1000, a2 * 1000, b * 1000, h * 1000));

        const int n = LiraBarSectionBuilder.DistributedPoints;
        double left = -b / 2 + a, right = b / 2 - a, bottom = -h / 2 + a1, top = h / 2 - a2;
        var bars = new List<LiraBarPoint>(4 * n);
        void Add(double x, double y, double cm2) { if (cm2 > 0) bars.Add(Point(x, y, cm2 * 1e-4 / n)); }
        for (int i = 0; i < n; i++)
        {
            double x = left + (right - left) * i / (n - 1);
            Add(x, bottom, s.As1!.Value);
            Add(x, top, s.As2!.Value);
        }
        for (int i = 1; i <= n; i++)
        {
            double y = bottom + (top - bottom) * i / (n + 1);
            Add(left, y, s.As3!.Value);
            Add(right, y, s.As4!.Value);
        }
        return (bars, null);
    }

    static LiraBarPoint Point(double x, double y, double areaM2) =>
        new(x, y, areaM2, Math.Sqrt(4 * areaM2 / Math.PI));
}

/// <summary>
/// Источник <see cref="FemCheckRebarSource.Selected"/> для стержней: сечение КЭ из подобранной SCAD арматуры
/// (выгрузка плагина) — своё для каждого сечения КЭ; у строки без номера сечения — огибающая по сечениям.
/// </summary>
public sealed class ScadSelectedBarSectionSource(ScadBarSectionContext context) : IBarElementSectionSource
{
    const string Label = "SCAD";
    const string NotInFile = "КЭ нет в подборе SCAD";

    readonly Dictionary<(int Elem, int Section), BarElementSection> _cache = [];

    /// <inheritdoc/>
    public string Key => FemCheckRebarSource.Selected;

    /// <inheritdoc/>
    public bool PerSection => true;

    /// <inheritdoc/>
    public string? MissingReason(FemCheckScopeElement element) =>
        context.SelectedBar(element) == null ? NotInFile
            : context.Profile(element).Reason ?? context.Covers(element).Reason;

    /// <inheritdoc/>
    public BarElementSection Resolve(FemCheckScopeElement element, int? sectionNum)
    {
        if (context.SelectedBar(element) is not { } bar)
            return BarElementSection.Missing(NotInFile, Label);
        var key = (bar.ElementId, sectionNum ?? 0);
        if (!_cache.TryGetValue(key, out var result))
            _cache[key] = result = Build(element, bar, sectionNum);
        return result;
    }

    BarElementSection Build(FemCheckScopeElement element, ScadSelectedBar bar, int? sectionNum)
    {
        var (profile, reason) = context.Profile(element);
        if (profile == null) return BarElementSection.Missing(reason!, Label);
        var (covers, coversReason) = context.Covers(element);
        if (covers is not { } c) return BarElementSection.Missing(coversReason!, Label);

        ScadSelectedBarSection? areas;
        if (sectionNum is int n)
        {
            if (n < 1 || n > bar.Sections.Count)
                return BarElementSection.Missing($"в подборе SCAD у КЭ нет сечения {n} (сечений: {bar.Sections.Count})", Label);
            areas = bar.Sections[n - 1];
            if (areas is not { IsComplete: true })
                return BarElementSection.Missing($"подбор SCAD в сечении {n} не выполнен", Label);
        }
        else
        {
            int failed = bar.Sections.ToList().FindIndex(s => s is not { IsComplete: true });
            if (failed >= 0)
                return BarElementSection.Missing($"подбор SCAD в сечении {failed + 1} не выполнен", Label);
            areas = bar.Envelope;
            if (areas == null) return BarElementSection.Missing("подбор SCAD для КЭ не выполнен", Label);
        }

        var (bars, layoutReason) = ScadBarSectionBuilder.SelectedLayout(areas, profile, c.A1, c.A2);
        if (bars == null) return BarElementSection.Missing(layoutReason!, Label);

        string tag = sectionNum is int s
            ? $"SCAD {LiraBarSectionBuilder.SizeLabel(profile)} э.{bar.ElementId} с{s}"
            : $"SCAD {LiraBarSectionBuilder.SizeLabel(profile)} э.{bar.ElementId}";
        return new BarElementSection(LiraBarSectionBuilder.Build(tag, profile, bars), Label, null);
    }
}
