using System.Globalization;
using CScore.PlateRebar;

namespace CScore.Fem;

/// <summary>Ключи источников армирования пластинчатого КЭ при проверке по КЭ.</summary>
public static class FemCheckRebarSource
{
    /// <summary>Одно сечение цели на все КЭ.</summary>
    public const string Section = "section";
    /// <summary>Заданное армирование КЭ (ТЗА ЛИРЫ из RBT).</summary>
    public const string Assigned = "assigned";
    /// <summary>Подобранное армирование КЭ (ASP ЛИРЫ).</summary>
    public const string Selected = "selected";
    /// <summary>Раскладка OpenCS: фон + зоны региона.</summary>
    public const string Layout = "layout";
}

/// <summary>Сечение пластинчатого КЭ, выданное источником армирования.</summary>
/// <param name="Section">Сечение; null — источник не даёт армирования для КЭ.</param>
/// <param name="Label">Подпись сечения в результате (имя сечения, «ТЗА 1 2 4», «ASP»).</param>
/// <param name="RebarKey">Ключ армирования: одинаков у КЭ с одинаковыми толщиной и слоями.</param>
/// <param name="Reason">Почему сечения нет (при <paramref name="Section"/> == null).</param>
public sealed record PlateElementSection(PlateSection? Section, string Label, string RebarKey, string? Reason)
{
    /// <summary>У КЭ нет армирования из этого источника.</summary>
    public static PlateElementSection Missing(string reason, string label = "") => new(null, label, "", reason);
}

/// <summary>
/// Источник сечения пластинчатого КЭ для проверки по КЭ: сечение-шаблон цели плюс толщина
/// и армирование конкретного КЭ. Не привязан к программе-источнику схемы.
/// </summary>
public interface IPlateElementSectionSource
{
    /// <summary>Ключ источника (<see cref="FemCheckRebarSource"/>).</summary>
    string Key { get; }

    /// <summary>Сечение КЭ. Вызывается последовательно, до параллельной части проверки.</summary>
    PlateElementSection Resolve(FemCheckScopeElement element);

    /// <summary>Предупреждения источника по КЭ цели (например, расхождение классов материалов с шаблоном).</summary>
    IReadOnlyList<string> Warnings(IReadOnlyList<FemCheckScopeElement> elements, Material? concrete, Material? rebar) => [];
}

/// <summary>Источник <see cref="FemCheckRebarSource.Section"/>: одно сечение цели на все КЭ.</summary>
public sealed class TemplatePlateSectionSource(PlateSection template) : IPlateElementSectionSource
{
    readonly PlateElementSection _section = new(template, template.Tag,
        PlateElementSectionFactory.Key(template.H, template.RebarLayers), null);

    /// <inheritdoc/>
    public string Key => FemCheckRebarSource.Section;

    /// <inheritdoc/>
    public PlateElementSection Resolve(FemCheckScopeElement element) => _section;
}

/// <summary>Источник, данных для которого у схемы нет (не приложен файл армирования): у всех КЭ — одна причина.</summary>
public sealed class UnavailablePlateSectionSource(string key, string reason) : IPlateElementSectionSource
{
    readonly PlateElementSection _missing = PlateElementSection.Missing(reason);

    /// <inheritdoc/>
    public string Key => key;

    /// <inheritdoc/>
    public PlateElementSection Resolve(FemCheckScopeElement element) => _missing;
}

/// <summary>Арматура сечения-шаблона у одной грани: привязки и диаметры по направлениям.</summary>
/// <param name="CoverX">Расстояние от грани до ц. т. арматуры вдоль x, м.</param>
/// <param name="CoverY">Расстояние от грани до ц. т. арматуры вдоль y, м.</param>
/// <param name="DiameterX">Диаметр арматуры вдоль x, м (0 — не задан).</param>
/// <param name="DiameterY">Диаметр арматуры вдоль y, м (0 — не задан).</param>
public sealed record PlateTemplateFace(double CoverX, double CoverY, double DiameterX, double DiameterY);

/// <summary>
/// Сборка сечений КЭ из сечения-шаблона: бетон, материалы, модель и диаграмма — от шаблона,
/// толщина и слои армирования — от КЭ. Одинаковые сочетания «толщина + слои» дают один объект.
/// </summary>
public sealed class PlateElementSectionFactory(PlateSection template)
{
    readonly Dictionary<string, PlateSection> _cache = new(StringComparer.Ordinal);

    /// <summary>Сечение-шаблон.</summary>
    public PlateSection Template => template;

    /// <summary>Арматура шаблона у грани Z+.</summary>
    public PlateTemplateFace? Top { get; } = Face(template, +1);

    /// <summary>Арматура шаблона у грани Z−.</summary>
    public PlateTemplateFace? Bottom { get; } = Face(template, -1);

    /// <summary>Ключ армирования: толщина + отпечаток слоёв.</summary>
    public static string Key(double thicknessM, IReadOnlyList<PlateRebarLayer> layers) =>
        thicknessM.ToString("R", CultureInfo.InvariantCulture) + "|" + PlateRebarLayoutFingerprint.Compute(layers);

    /// <summary>Сечение КЭ с заданной толщиной и слоями (слои не копируются — не менять после вызова).</summary>
    public (PlateSection Section, string Key) Get(double thicknessM, List<PlateRebarLayer> layers)
    {
        string key = Key(thicknessM, layers);
        if (!_cache.TryGetValue(key, out var section))
        {
            section = template.CloneForCalc();
            section.H = thicknessM;
            section.RebarLayers = layers;
            _cache[key] = section;
        }
        return (section, key);
    }

    /// <summary>
    /// Арматура шаблона у грани: привязка — расстояние от грани до ц. т. слоёв этой грани
    /// (среднее по площади), диаметр — первого слоя с заданным диаметром. Null — слоёв у грани нет.
    /// </summary>
    static PlateTemplateFace? Face(PlateSection section, int sign)
    {
        double half = section.H / 2.0;
        double ax = 0, mx = 0, ay = 0, my = 0, dx = 0, dy = 0, fallback = double.NaN;
        foreach (var l in section.RebarLayers)
        {
            double z = l.Asx > 0 ? l.Zsx : l.Zsy;
            if (Math.Sign(z) != sign) continue;
            if (double.IsNaN(fallback)) fallback = half - Math.Abs(z);
            if (l.Asx > 0) { ax += l.Asx; mx += l.Asx * (half - Math.Abs(l.Zsx)); }
            if (l.Asy > 0) { ay += l.Asy; my += l.Asy * (half - Math.Abs(l.Zsy)); }
            if (dx <= 0 && l.DiameterX > 0) dx = l.DiameterX;
            if (dy <= 0 && l.DiameterY > 0) dy = l.DiameterY;
        }
        if (double.IsNaN(fallback)) return null;
        double coverX = ax > 0 ? mx / ax : ay > 0 ? my / ay : fallback;
        double coverY = ay > 0 ? my / ay : coverX;
        return new PlateTemplateFace(coverX, coverY, dx, dy);
    }
}
