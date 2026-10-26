namespace CScore;

/// <summary>Способ учёта деформаций, накопленных до включения части.</summary>
public enum StageStrainMode
{
    /// <summary>Часть видит плоскость K − K_k (K_k — плоскость в момент включения).</summary>
    Plane,
    /// <summary>Волокна части несут начальную деформацию стадии; контурные области разбиваются на волокна.</summary>
    Fibers,
}

/// <summary>
/// Стадийное сечение: упорядоченная очередь частей, включаемых в работу по ходу истории нагружения.
/// Часть 0 работает с начала; часть k включается при текущей плоскости K_k и далее воспринимает
/// только приращение деформаций (в момент включения её напряжения нулевые).
/// <see cref="CrossSection.Areas"/> — объединение областей всех частей (геометрия, габариты, отрисовка).
/// </summary>
public class StagedSection : CrossSection
{
    Kurvature[] _planes = [];

    /// <summary>Части в порядке включения.</summary>
    public List<SectionPart> Parts { get; set; } = [];

    /// <summary>Способ учёта деформаций стадий (транзитно, задаётся расчётом).</summary>
    [System.Text.Json.Serialization.JsonIgnore]
    public StageStrainMode StrainMode { get; set; } = StageStrainMode.Plane;

    /// <summary>Число включённых частей (транзитно).</summary>
    [System.Text.Json.Serialization.JsonIgnore]
    public int ActivePartCount { get; private set; } = 1;

    /// <summary>Плоскости включения частей (транзитно); у части 0 — нулевая.</summary>
    [System.Text.Json.Serialization.JsonIgnore]
    public IReadOnlyList<Kurvature> ActivationPlanes
    {
        get { EnsurePlanes(); return _planes; }
    }

    /// <summary>Пересобирает <see cref="CrossSection.Areas"/> из областей частей.</summary>
    public void SyncAreas() => Areas = Parts.SelectMany(p => p.Areas).ToList();

    /// <summary>Возврат к началу истории: включена только часть 0, плоскости включения нулевые.</summary>
    public virtual void ResetHistory()
    {
        ActivePartCount = Parts.Count > 0 ? 1 : 0;
        _planes = new Kurvature[Parts.Count];
    }

    /// <summary>Включает часть <paramref name="index"/> при текущей плоскости <paramref name="current"/>.</summary>
    /// <exception cref="InvalidOperationException">Часть включается не по порядку или уже включена.</exception>
    public virtual void ActivatePart(int index, Kurvature current)
    {
        EnsurePlanes();
        if (index != ActivePartCount || index >= Parts.Count)
            throw new InvalidOperationException(
                $"Часть {index} нельзя включить: включено частей {ActivePartCount} из {Parts.Count}.");
        _planes[index] = current;
        ActivePartCount = index + 1;
    }

    /// <summary>Включает все части с нулевыми плоскостями (расчёт «без истории»).</summary>
    public virtual void ActivateAll()
    {
        ResetHistory();
        ActivePartCount = Parts.Count;
    }

    /// <summary>Области включённых частей.</summary>
    public IEnumerable<MaterialArea> ActiveAreas() => Parts.Take(ActivePartCount).SelectMany(p => p.Areas);

    /// <summary>Плоскость, которую видит материал части <paramref name="partIndex"/> при общей плоскости <paramref name="k"/>.</summary>
    public Kurvature RelativePlane(int partIndex, Kurvature k) => k - ActivationPlanes[partIndex];

    /// <inheritdoc/>
    /// <remarks>Режим <see cref="StageStrainMode.Plane"/>: часть k получает K − K_k.</remarks>
    public override IEnumerable<(MaterialArea area, Kurvature k)> EnumerateAreas(Kurvature baseK)
    {
        EnsurePlanes();
        for (int i = 0; i < ActivePartCount; i++)
        {
            var ka = StrainMode == StageStrainMode.Plane ? baseK - _planes[i] : baseK;
            foreach (var a in Parts[i].Areas)
                if (MaterialArea.IsCalcActive(a)) yield return (a, ka);
        }
    }

    /// <inheritdoc/>
    /// <remarks>Упругая оценка по включённым частям без учёта плоскостей включения — запасной путь
    /// (решатель истории стартует тёпло с предыдущего шага).</remarks>
    public override Kurvature Guess(Load load)
    {
        var active = ActiveAreas().ToList();
        return GuessFromProps(ElasticProps(active), load - PrestressElasticLoad(active));
    }

    /// <inheritdoc/>
    public override GeoProps ElasticGeoProps() => ElasticProps(ActiveAreas());

    /// <inheritdoc/>
    /// <remarks>Диаграммы строятся по частям: бетон-носитель арматуры ищется только в её части.</remarks>
    public override void ResolveAndBuildDiagramms(double sp63EtaMin = 0.85,
        IReadOnlyList<Diagramm>? pool = null, bool rebarDifferentialDiagram = true, double ekbEtaMin = 0.05)
    {
        SyncAreas();
        foreach (var part in Parts)
            BuildDiagramms(part.Areas, sp63EtaMin, pool, rebarDifferentialDiagram, ekbEtaMin);
        EnsurePlanes();
    }

    /// <inheritdoc/>
    public override CrossSection CloneForCalc()
    {
        var c = new StagedSection
        {
            Id = Id, Num = Num, Tag = Tag, Description = Description,
            Parts = Parts.Select(p => p.CloneForCalc()).ToList(),
            StrainMode = StrainMode,
        };
        c.SyncAreas();
        c._planes = ActivationPlanes.ToArray();
        c.ActivePartCount = ActivePartCount;
        return c;
    }

    void EnsurePlanes()
    {
        if (_planes.Length != Parts.Count)
        {
            var old = _planes;
            _planes = new Kurvature[Parts.Count];
            Array.Copy(old, _planes, Math.Min(old.Length, _planes.Length));
            ActivePartCount = Math.Clamp(ActivePartCount, Parts.Count > 0 ? 1 : 0, Parts.Count);
        }
    }
}
