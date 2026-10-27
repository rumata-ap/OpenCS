namespace CScore;

/// <summary>Способ учёта деформаций, накопленных до включения части.</summary>
public enum StageStrainMode
{
    /// <summary>Часть видит плоскость K − K_k (K_k — плоскость в момент включения).</summary>
    Plane,
    /// <summary>Волокна части несут начальную деформацию <see cref="Fiber.Eps_stage"/>; контурные области разбиваются на волокна.</summary>
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
        foreach (var f in Parts.SelectMany(p => p.Areas).SelectMany(a => a.Fibers))
            f.Eps_stage = 0;
        _field?.Reset();
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
        if (StrainMode == StageStrainMode.Fibers)
            StampActivationStrain(index);
    }

    /// <summary>
    /// Подготовка режима «по волокнам»: контурные области без сетки разбиваются на волокна по своим
    /// настройкам сетки (Grid — SliceXY, иначе триангуляция). Вызывать на копии CloneForCalc.
    /// </summary>
    public void PrepareFiberMode()
    {
        foreach (var area in Parts.SelectMany(p => p.Areas))
        {
            if (!MaterialArea.IsCalcActive(area) || area.Hull == null) continue;
            if (area.Fibers.Any(f => f.TypeFiber != FiberType.point)) continue;
            if (area.MeshMethod == MeshMethod.Grid)
                area.SliceXY(area.NX > 0 ? area.NX : 12, area.NY > 0 ? area.NY : 20);
            else area.Triangulate(area.MeshMaxArea, area.MeshMinAngle, area.MeshMethod,
                                  area.MeshMaxEdgeLen, area.MeshSmoothIter);
        }
    }

    /// <summary>Записывает волокнам части деформацию плоскости её включения.</summary>
    void StampActivationStrain(int index)
    {
        var k = _planes[index];
        foreach (var f in Parts[index].Areas.SelectMany(a => a.Fibers))
            f.Eps_stage = k.e0 + k.ky * f.Y + k.kz * f.X;
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
        if (StrainMode == StageStrainMode.Fibers)
            for (int i = 1; i < c.ActivePartCount; i++) c.StampActivationStrain(i);
        return c;
    }

    Hysteresis.FiberStateField? _field;
    CalcType _fieldCalc;

    /// <summary>Включён материал с состоянием (гистерезис, транзитно).</summary>
    [System.Text.Json.Serialization.JsonIgnore]
    public bool HysteresisEnabled => _field is not null;

    /// <summary>
    /// Включает гистерезис: режим «по волокнам» (контурные области разбиваются на волокна), модели материала для всех
    /// областей частей, начальные состояния. Вызывать на копии CloneForCalc после ResetHistory/ActivateAll;
    /// CloneForCalc гистерезис не копирует.
    /// </summary>
    /// <param name="rebarRupture">εs2 арматурной области с ролью «Разрушается», иначе null.</param>
    /// <param name="softening">Длина спада для ролей «Разрушается».</param>
    public void EnableHysteresis(Hysteresis.HysteresisOptions o, CalcType calc,
        Func<MaterialArea, double?> rebarRupture, double softening)
    {
        StrainMode = StageStrainMode.Fibers;
        PrepareFiberMode();
        EnsurePlanes();
        for (int i = 1; i < ActivePartCount; i++) StampActivationStrain(i);
        _fieldCalc = calc;
        _field = new Hysteresis.FiberStateField(Parts.SelectMany(p => p.Areas), calc, o, rebarRupture, softening);
    }

    /// <summary>Выключает гистерезис (возврат к нелинейно-упругому материалу).</summary>
    public void DisableHysteresis() => _field = null;

    /// <summary>Фиксирует состояния волокон включённых частей при плоскости k.</summary>
    public void CommitState(Kurvature k, bool ten) => _field?.Commit(EnumerateAreas(k), ten);

    /// <summary>Остаточные деформации части после пробы плоскости k (без фиксации); (0, 0) без гистерезиса.</summary>
    public (double Concrete, double Rebar) ResidualStrains(int partIndex, Kurvature k, bool ten) =>
        _field is null ? (0, 0)
            : _field.Residual(EnumerateAreas(k).Where(p => Parts[partIndex].Areas.Contains(p.area)), ten);

    /// <inheritdoc/>
    /// <remarks>При гистерезисе — пробный интеграл от зафиксированных состояний волокон (<paramref name="ca"/> не
    /// используется: в истории нагружения сжатая арматура всегда работает).</remarks>
    public override Load Integral(Kurvature k, CalcType calc = CalcType.C, bool ten = true, bool ca = true) =>
        _field is not null && calc == _fieldCalc ? _field.Integrate(EnumerateAreas(k), ten, writeFibers: false)
            : base.Integral(k, calc, ten, ca);

    /// <inheritdoc/>
    /// <remarks>При гистерезисе волокнам пишутся пробные σ от зафиксированных состояний (точки контура не
    /// обновляются).</remarks>
    public override void SetEps(Kurvature k, CalcType calc, bool ten = true, bool ca = true)
    {
        if (_field is not null && calc == _fieldCalc) _field.Integrate(EnumerateAreas(k), ten, writeFibers: true);
        else base.SetEps(k, calc, ten, ca);
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
