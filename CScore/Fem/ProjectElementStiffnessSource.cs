namespace CScore.Fem;

/// <summary>
/// Свойства КЭ своей схемы по сечениям проекта. Стержни — сечение КЭ (<see cref="FemElement.CrossSectionId"/>, иначе
/// конструктивного элемента): приведённые к модулю E = EA/A характеристики относительно центра жёсткости (модули
/// материалов областей, не состояние волокон; ось X сечения → местная z, Y → местная y), ν по преобладающему
/// материалу (бетон 0,2, прочее 0,3), GJ — ручное значение или задача кручения КЭ/КонЭ, иначе полярный момент
/// Ix + Iy. Пластины — сечение пластины конструктивного элемента (<see cref="FemMember.PlateSectionId"/>): толщина H,
/// E бетона, ν = 0,2. Плотности у материалов нет — удельный вес по умолчанию: бетон <see cref="ConcreteUnitWeight"/>,
/// сталь и арматура <see cref="SteelUnitWeight"/> (у стержня — средний по площадям областей).
/// </summary>
public sealed class ProjectElementStiffnessSource : IFemElementStiffnessSource
{
    /// <summary>Удельный вес бетона и железобетона по умолчанию, Н/м³.</summary>
    public const double ConcreteUnitWeight = 25e3;

    /// <summary>Удельный вес стали по умолчанию, Н/м³.</summary>
    public const double SteelUnitWeight = 78.5e3;

    const double ConcreteNu = 0.2, SteelNu = 0.3, KPa = 1e3;

    readonly IReadOnlyDictionary<string, FemMember> _members;
    readonly IReadOnlyDictionary<int, CrossSection> _sections;
    readonly IReadOnlyDictionary<int, PlateSection> _plates;
    readonly IReadOnlyDictionary<int, Material> _materials;
    readonly Func<int, double?>? _torsionGJ;
    readonly Dictionary<int, BarSection?> _bars = new();

    /// <summary>Свойства сечения стержня без кручения, J по умолчанию и удельный вес.</summary>
    sealed record BarSection(double E, double Nu, double A, double Iy, double Iz, double PolarJ, double UnitWeight);

    /// <param name="members">Конструктивные элементы схемы.</param>
    /// <param name="sections">Сечения стержней проекта (с волокнами).</param>
    /// <param name="plates">Сечения пластин проекта.</param>
    /// <param name="materials">Материалы проекта (модуль <see cref="Material.E"/> — кПа, как в БД: B20 — 27,5·10⁶).</param>
    /// <param name="torsionGJ">GJ, Н·м², по номеру задачи кручения; null — задачи не учитываются.</param>
    public ProjectElementStiffnessSource(IEnumerable<FemMember> members, IEnumerable<CrossSection> sections,
        IEnumerable<PlateSection> plates, IEnumerable<Material> materials, Func<int, double?>? torsionGJ = null)
    {
        _members = members.Where(m => m.ElemTag.Length > 0).GroupBy(m => m.ElemTag, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.Ordinal);
        _sections = sections.GroupBy(s => s.Id).ToDictionary(g => g.Key, g => g.First());
        _plates = plates.GroupBy(s => s.Id).ToDictionary(g => g.Key, g => g.First());
        _materials = materials.GroupBy(m => m.Id).ToDictionary(g => g.Key, g => g.First());
        _torsionGJ = torsionGJ;
    }

    /// <inheritdoc/>
    public FemShellStiffness? Shell(FemElement element)
    {
        if (element.ElemType != "shell" || Plate(element) is not { } p || !(p.H > 0)) return null;
        return _materials.TryGetValue(p.ConcreteMaterialId, out var c) && c.E > 0
            ? new FemShellStiffness(c.E * KPa, ConcreteNu, p.H) : null;
    }

    /// <inheritdoc/>
    public FemBarStiffness? Bar(FemElement element)
    {
        if (BarOf(element) is not { } s) return null;
        double g = s.E / (2 * (1 + s.Nu));
        var member = Member(element);
        string strategy = element.CrossSectionId != null || member == null ? element.GjStrategy : member.GjStrategy;
        double? gj = strategy switch
        {
            "manual" => element.GjManualValue ?? member?.GjManualValue,
            "saint_venant" when (element.GjTorsionTaskId ?? member?.GjTorsionTaskId) is { } task => _torsionGJ?.Invoke(task),
            _ => null,
        };
        return new FemBarStiffness(s.E, g, s.A, s.Iy, s.Iz, gj is > 0 ? gj.Value / g : s.PolarJ);
    }

    /// <inheritdoc/>
    public double? UnitWeight(FemElement element) => element.ElemType switch
    {
        "shell" => Shell(element) != null ? ConcreteUnitWeight : null,
        _ => BarOf(element)?.UnitWeight,
    };

    /// <inheritdoc/>
    public double? BarArea(FemElement element) => BarOf(element)?.A;

    FemMember? Member(FemElement element) =>
        element.SourceMemberTag is { } tag && _members.TryGetValue(tag, out var m) ? m : null;

    PlateSection? Plate(FemElement element) =>
        Member(element)?.PlateSectionId is { } id && _plates.TryGetValue(id, out var p) ? p : null;

    BarSection? BarOf(FemElement element)
    {
        if (element.ElemType == "shell") return null;
        if ((element.CrossSectionId ?? Member(element)?.CrossSectionId) is not { } id) return null;
        if (_bars.TryGetValue(id, out var cached)) return cached;
        return _bars[id] = _sections.TryGetValue(id, out var cs) ? Compute(cs) : null;
    }

    BarSection? Compute(CrossSection section)
    {
        var total = new GeoProps();
        double weight = 0, concreteEA = 0;
        foreach (var area in section.Areas)
        {
            if (!MaterialArea.IsCalcActive(area)) continue;
            var material = area.Material ?? _materials.GetValueOrDefault(area.MaterialId);
            if (material is not { E: > 0 }) continue;
            var p = new GeoProps(area, material.E * KPa);
            total += p;
            bool concrete = IsConcrete(material);
            weight += p.A * (concrete ? ConcreteUnitWeight : SteelUnitWeight);
            if (concrete) concreteEA += p.EA;
        }
        if (!(total.A > 0) || !(total.EA > 0)) return null;
        double e = total.EA / total.A, cx = total.ESy / total.EA, cy = total.ESx / total.EA;
        double eiAboutY = total.EIy - total.EA * cx * cx, eiAboutX = total.EIx - total.EA * cy * cy;
        double iy = eiAboutY / e, iz = eiAboutX / e;
        if (!(iy > 0) || !(iz > 0)) return null;
        return new BarSection(e, concreteEA >= total.EA / 2 ? ConcreteNu : SteelNu, total.A, iy, iz, iy + iz,
            weight / total.A);
    }

    static bool IsConcrete(Material m) =>
        m.Type == MatType.Concrete || (m.Type == MatType.Custom && m.BaseType == MatType.Concrete);
}
