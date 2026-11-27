using System.Collections.ObjectModel;
using System.Globalization;
using System.IO;
using CScore.Fem;
using CScore.Fem.Fields;
using OpenCS.Services;
using OpenCS.Utilites;

namespace OpenCS.ViewModels;

/// <summary>
/// Строка привязки столбца значений: компонента величины и единица ввода. Смена компоненты пересобирает список единиц
/// (у поворотных жёсткостей пружин — моментные); компонента <see cref="Skip"/> — столбец не используется.
/// </summary>
public sealed class FemFieldColumnRow : ViewModelBase
{
    /// <summary>Компонента «не использовать»: посторонний числовой столбец (номер точки, примечание) в поле не идёт.</summary>
    public const string Skip = "—";

    readonly string _quantity;
    readonly double _tonToKn;
    string _component;
    FemFieldUnit _unit;

    public FemFieldColumnRow(int columnIndex, string header, string quantity, string component, double tonToKn, FemFieldUnit? unit = null)
    {
        ColumnIndex = columnIndex;
        Header = header;
        _quantity = quantity;
        _tonToKn = tonToKn;
        _component = component;
        Components = [Skip, .. FemFieldQuantities.Get(quantity).Components];
        Units = FemFieldQuantities.Units(quantity, component == Skip ? Components[1] : component, tonToKn);
        _unit = unit == null ? Units[0] : Units.FirstOrDefault(u => u == unit) ?? Add(unit);
    }

    /// <summary>Номер столбца таблицы (у поля, открытого на правку, — столбец восстановленной таблицы точек).</summary>
    public int ColumnIndex { get; }
    public string Header { get; }
    public IReadOnlyList<string> Components { get; }
    public IReadOnlyList<FemFieldUnit> Units { get; private set; }
    public bool IsUsed => _component != Skip;

    public string Component
    {
        get => _component;
        set
        {
            if (value == null || _component == value) return;
            _component = value;
            OnPropertyChanged(nameof(IsUsed));
            if (value == Skip) { OnPropertyChanged(); return; }
            string label = _unit.Label;
            Units = FemFieldQuantities.Units(_quantity, value, _tonToKn);
            _unit = Units.FirstOrDefault(u => u.Label == label) ?? Units[0];
            OnPropertyChanged();
            OnPropertyChanged(nameof(Units));
            OnPropertyChanged(nameof(Unit));
        }
    }

    // Смена списка единиц (другая компонента) сбрасывает выбор ComboBox и пишет сюда null — его не принимаем.
    public FemFieldUnit Unit { get => _unit; set { if (value == null) return; _unit = value; OnPropertyChanged(); } }

    /// <summary>Единица сохранённого поля, которой нет в реестре (другой множитель тонны-силы), — дописывается в список.</summary>
    FemFieldUnit Add(FemFieldUnit unit)
    {
        Units = [unit, .. Units];
        return unit;
    }
}

/// <summary>
/// Диалог поля из таблицы точек (спека § 5.3): таблица и столбцы, величина и способ, совмещение с автоподбором,
/// предпросмотр переноса на цели. Окно — <c>FemFieldDialog</c>; сохранение — у вызывающего кода.
/// </summary>
public sealed class FemFieldDialogVM : ViewModelBase
{
    readonly FemFieldMeshContext _ctx;
    readonly FemFieldTargets _targets;
    readonly double _tonToKn;
    readonly int _existingId;
    PointTable? _table;

    // Поле, открытое на правку: его точки действуют, пока не загружена новая таблица.
    IReadOnlyList<FieldPoint>? _existingPoints;
    readonly FemFieldDefinition? _existingDefinition;
    readonly bool _existingHasZ;

    public FemFieldDialogVM(FemFieldMeshContext ctx, FemFieldTargets targets, double tonToKn, FemField? existing = null)
    {
        _ctx = ctx;
        _targets = targets;
        _tonToKn = tonToKn;
        if (existing != null)
        {
            _existingId = existing.Id;
            Name = existing.Name;
            Quantity = existing.Quantity;
            var a = existing.Definition.Alignment;
            (LengthScale, MirrorX, MirrorY, AngleDeg, Dx, Dy, Dz) = (a.Scale, a.MirrorX, a.MirrorY, a.AngleDeg, a.Dx, a.Dy, a.Dz);
            var s = existing.Definition.Settings;
            (Method, MaxDistance, PlaneTolerance, MatchTolerance) = (s.Method, s.MaxDistance, s.PlaneTolerance, s.MatchTolerance);
            _existingPoints = existing.Points;
            _existingDefinition = existing.Definition;
            _existingHasZ = existing.HasZ;
            SourceFile = existing.SourceFile;
            var d = existing.Definition;
            string? zHeader = existing.HasZ ? d.ZHeader ?? "Z" : null;
            // Сохранённая таблица — снова таблица окна: выбор X/Y/Z и столбцы значений видны и правятся, как после
            // загрузки (раньше списки X/Y были пусты, а точки брались из поля скрыто).
            _table = PointTable.FromPoints(d.XHeader ?? "X", d.YHeader ?? "Y", zHeader, d.Columns.Select(c => c.Header).ToList(),
                existing.Points);
            Headers = _table.Headers;
            (XColumn, YColumn, ZColumn) = (0, 1, zHeader != null ? 2 : -1);
            int offset = zHeader != null ? 3 : 2;
            for (int k = 0; k < d.Columns.Count; k++)
            {
                var c = d.Columns[k];
                Columns.Add(new FemFieldColumnRow(offset + k, c.Header, Quantity, c.Component, tonToKn, new FemFieldUnit(c.UnitLabel, c.UnitFactor)));
            }
        }
    }

    public string Name { get; set; } = "";
    public string? SourceFile { get; private set; }
    public IReadOnlyList<string> Headers { get; private set; } = [];
    public int XColumn { get; set; }
    public int YColumn { get; set; } = 1;
    public int ZColumn { get; set; } = -1;
    public double LengthScale { get; set; } = 1;
    public string Quantity { get; private set; } = FemFieldQuantities.FoundationC1;
    public ObservableCollection<FemFieldColumnRow> Columns { get; } = [];
    public FieldMethod Method { get; set; } = FieldMethod.Interpolate;
    public double MaxDistance { get; set; } = 1;
    public double PlaneTolerance { get; set; } = 0.5;
    public double MatchTolerance { get; set; } = 0.5;
    public bool MirrorX { get; set; }
    public bool MirrorY { get; set; }
    public double AngleDeg { get; set; }
    public double Dx { get; set; }
    public double Dy { get; set; }
    public double Dz { get; set; }
    /// <summary>Последний автоподбор совмещения и его невязка; null — не подбиралось.</summary>
    public FieldFitResult? Fit { get; private set; }
    public string FitSummary { get; private set; } = "";
    public FieldEvaluation? Evaluation { get; private set; }
    /// <summary>Узлы-цели последнего предпросмотра (для пружин); у пластин — пусто.</summary>
    public IReadOnlyList<FieldNode> NodeTargets { get; private set; } = [];
    public string Summary { get; private set; } = "";

    /// <summary>Цели — узлы сетки (пружины): способ только «один к одному».</summary>
    public bool IsNodes => FemFieldQuantities.Get(Quantity).Target == FemFieldTargetKind.MeshNode;

    /// <summary>
    /// Разбирает таблицу и угадывает столбцы: X, Y, Z — по началу заголовка (Z — только если в столбце есть числа),
    /// остальные числовые — значения; величина — пружина, если есть столбец Rz/Kz/Rx/Kx/Ry/Ky, иначе C1. Единицы
    /// длины и значений — по заголовкам. Допуски — по размеру КЭ целей.
    /// </summary>
    public void LoadText(string text, string? fileName)
    {
        _table = PointTable.Parse(text);
        SourceFile = fileName;
        _existingPoints = null;
        Headers = _table.Headers;
        int Find(string prefix) => Headers.ToList().FindIndex(h => h.TrimStart().StartsWith(prefix, StringComparison.OrdinalIgnoreCase));
        XColumn = Math.Max(0, Find("X"));
        YColumn = Find("Y") is int y && y >= 0 ? y : Math.Min(1, Headers.Count - 1);
        ZColumn = Find("Z") is int z && z >= 0 && _table.ColumnHasNumbers(z) ? z : -1;
        LengthScale = Headers.Count > 0 ? PointTable.LengthScaleFromHeader(Headers[XColumn]) : 1;
        if (string.IsNullOrWhiteSpace(Name) && fileName != null) Name = Path.GetFileNameWithoutExtension(fileName);

        // У поля, открытого на правку («Повторить с новой таблицей»), величина остаётся прежней: цели заданы под неё.
        if (_existingDefinition == null)
        {
            bool spring = ValueColumns().Any(i => SpringComponent(Headers[i]) != null);
            Quantity = spring ? FemFieldQuantities.Spring : FemFieldQuantities.FoundationC1;
        }
        RebuildValueColumns();
        SetQuantity(Quantity);
        ApplyTableDefaults();
    }

    /// <summary>Столбцы значений — все числовые, кроме выбранных X, Y, Z (после ручной правки выбора координат).</summary>
    public void RebuildValueColumns()
    {
        if (_table == null) return;
        SetRows(ValueColumns().Select(i => (i, Headers[i])).ToList());
        OnPropertyChanged(string.Empty);
    }

    IEnumerable<int> ValueColumns() => Enumerable.Range(0, Headers.Count)
        .Where(i => i != XColumn && i != YColumn && i != ZColumn && _table!.ColumnHasNumbers(i));

    /// <summary>
    /// Строки столбцов значений: компонента и единица — по заголовку. Компоненту сначала получают столбцы с узнанным
    /// заголовком (Rz, C1, единица в заголовке); столбец, чья компонента уже занята, — «не использовать» (иначе номер
    /// точки или примечание затёрли бы значение).
    /// </summary>
    void SetRows(IReadOnlyList<(int Index, string Header)> cols)
    {
        var rows = new FemFieldColumnRow[cols.Count];
        var taken = new HashSet<string>(StringComparer.Ordinal);
        foreach (int i in Enumerable.Range(0, cols.Count).OrderBy(i => Recognized(cols[i].Header) ? 0 : 1))
        {
            string comp = DefaultComponent(Quantity, cols[i].Header);
            var unit = GuessUnit(cols[i].Header, FemFieldQuantities.Units(Quantity, comp, _tonToKn));
            rows[i] = new FemFieldColumnRow(cols[i].Index, cols[i].Header, Quantity, taken.Add(comp) ? comp : FemFieldColumnRow.Skip,
                _tonToKn, unit);
        }
        Columns.Clear();
        foreach (var r in rows) Columns.Add(r);
    }

    static bool Recognized(string header)
    {
        string h = header.TrimStart().ToLowerInvariant();
        return SpringComponent(header) != null || UnitLabel(header) != null || h.StartsWith('c') || h.StartsWith('с');
    }

    IEnumerable<FemFieldColumnRow> UsedColumns => Columns.Where(c => c.IsUsed);

    /// <summary>
    /// Меняет величину: компоненты и единицы столбцов пересобираются по заголовкам; у узловых целей способ —
    /// «один к одному», у пластин — интерполяция с допусками по размеру КЭ.
    /// </summary>
    public void SetQuantity(string code)
    {
        Quantity = code;
        SetRows(Columns.Select(r => (r.ColumnIndex, r.Header)).ToList());
        if (IsNodes) Method = FieldMethod.Match;
        else if (Method == FieldMethod.Match) Method = FieldMethod.Interpolate;
        OnPropertyChanged(string.Empty);
    }

    /// <summary>Допуски и наибольшее расстояние по размеру КЭ целей — только для новой таблицы; введённое не сбрасывается.</summary>
    void ApplyTableDefaults()
    {
        if (IsNodes) return;
        var defaults = FieldDefaults.For(TargetSet().Shells);
        // Три значащие цифры: 0,949 вместо 0,9486414102 — это оценка по размеру КЭ, а не точное число.
        (MaxDistance, PlaneTolerance) = (Round3(defaults.MaxDistance), Round3(defaults.PlaneTolerance));
        OnPropertyChanged(string.Empty);
    }

    static double Round3(double v) =>
        v > 0 && double.IsFinite(v) ? Math.Round(v, Math.Max(0, 2 - (int)Math.Floor(Math.Log10(v)))) : v;

    /// <summary>Компонента по заголовку (Rz → kz…); иначе у пружины — kz (осадка свай), у C1 — c1.</summary>
    static string DefaultComponent(string quantity, string header)
    {
        var components = FemFieldQuantities.Get(quantity).Components;
        if (SpringComponent(header) is { } c && components.Contains(c)) return c;
        return components.Contains("kz") ? "kz" : components[0];
    }

    static string? SpringComponent(string header)
    {
        string h = header.TrimStart().ToLowerInvariant();
        foreach (var (p, c) in new[] { ("rz", "kz"), ("kz", "kz"), ("rx", "kx"), ("kx", "kx"), ("ry", "ky"), ("ky", "ky") })
            if (h.StartsWith(p, StringComparison.Ordinal)) return c;
        return null;
    }

    static FemFieldUnit GuessUnit(string header, IReadOnlyList<FemFieldUnit> units) =>
        units.FirstOrDefault(u => u.Label == UnitLabel(header)) ?? units[0];

    /// <summary>Единица по заголовку («Rz [тс/см]», «C1, кН/м3», «Kux кН*м/рад»); null — не узнана.</summary>
    static string? UnitLabel(string header)
    {
        string h = header.ToLowerInvariant().Replace(" ", "").Replace("*", "·").Replace("^3", "3").Replace("³", "3")
            .Replace("kn", "кн").Replace("mn", "мн").Replace("/m", "/м").Replace("·m", "·м").Replace("t_m3", "тс/м3");
        // Порядок важен: «тс/см» раньше «тс/м», «кН/м» и «МН/м» раньше «Н/м», объёмные и моментные раньше линейных.
        foreach (var (pattern, label) in new[]
        {
            ("тс·м/рад", "тс·м/рад"), ("кн·м/рад", "кН·м/рад"), ("мн·м/рад", "МН·м/рад"), ("н·м/рад", "Н·м/рад"),
            ("тс/м3", "тс/м³"), ("т/м3", "тс/м³"), ("кн/м3", "кН/м³"), ("мн/м3", "МН/м³"), ("н/м3", "Н/м³"),
            ("тс/см", "тс/см"), ("тс/м", "тс/м"), ("кн/м", "кН/м"), ("мн/м", "МН/м"), ("н/м", "Н/м"),
        })
            if (h.Contains(pattern, StringComparison.Ordinal)) return label;
        return null;
    }

    FieldAlignment Alignment => new(LengthScale, MirrorX, MirrorY, AngleDeg, Dx, Dy, Dz);

    FemFieldTargetSet TargetSet() => FemFieldEvaluator.ResolveTargets(
        new FemField { Quantity = Quantity, Definition = new FemFieldDefinition { Targets = _targets } }, _ctx);

    (List<FieldPoint> Points, bool HasZ) RawPoints()
    {
        var used = UsedColumns.Select(c => c.ColumnIndex).ToList();
        // Поле без новой таблицы: номер столбца строки — номер значения точки.
        if (_table == null)
            return ((_existingPoints ?? []).Select(p => new FieldPoint(p.P, used.Select(k => p.Values[k]).ToArray())).ToList(), _existingHasZ);
        var (pts, _) = _table.Select(XColumn, YColumn, ZColumn >= 0 ? ZColumn : null, used);
        return (pts, ZColumn >= 0);
    }

    List<FieldVec3> TargetPoints()
    {
        var set = TargetSet();
        return IsNodes ? set.Nodes.Select(n => n.P).ToList() : set.Shells.SelectMany(s => s.Nodes).ToList();
    }

    /// <summary>Сдвиг по габаритам: центры охватывающих параллелепипедов таблицы и целей совмещаются.</summary>
    public void FitByExtents()
    {
        var (points, hasZ) = RawPoints();
        ApplyFit(FieldAlignmentFit.ByExtents(Alignment, points.Select(p => p.P).ToList(), TargetPoints(), hasZ));
    }

    /// <summary>Сдвиг по точечным целям (сваи ↔ узлы): итерации «ближайшая цель → средний сдвиг».</summary>
    public void FitByPoints()
    {
        var (points, hasZ) = RawPoints();
        ApplyFit(FieldAlignmentFit.ByPoints(Alignment, points.Select(p => p.P).ToList(), TargetPoints(), hasZ));
    }

    void ApplyFit(FieldFitResult fit)
    {
        Fit = fit;
        (Dx, Dy, Dz) = (fit.Alignment.Dx, fit.Alignment.Dy, fit.Alignment.Dz);
        FitSummary = string.Format(CultureInfo.CurrentCulture, Loc.S("FemFieldFitSummary"), fit.Median, fit.P95, fit.Max, fit.Count);
        OnPropertyChanged(string.Empty);
    }

    /// <summary>Перенос на цели без сохранения: результат и сводка способов получения значений.</summary>
    public void Preview()
    {
        var computed = FemFieldEvaluator.Compute(BuildField(0), _ctx);
        Evaluation = computed.Evaluation;
        NodeTargets = IsNodes ? TargetSet().Nodes : [];
        var t = Evaluation.Targets;
        Summary = string.Format(CultureInfo.CurrentCulture, Loc.S("FemFieldPreviewSummary"),
            t.Count, t.Count(x => x.How == FieldHow.Interpolated), t.Count(x => x.How == FieldHow.Nearest),
            t.Count(x => x.How == FieldHow.Idw), t.Count(x => x.How == FieldHow.Matched), t.Count(x => x.How == FieldHow.None),
            Evaluation.PointCount, Evaluation.PointsUnused);
        OnPropertyChanged(nameof(Evaluation));
        OnPropertyChanged(nameof(NodeTargets));
        OnPropertyChanged(nameof(Summary));
    }

    /// <summary>Подписи плоских участков последнего предпросмотра: номер, число КЭ, нормаль.</summary>
    public IReadOnlyList<string> PatchLabels => Evaluation?.Patches.Select(p => string.Format(CultureInfo.CurrentCulture,
        Loc.S("FemFieldPatchLabel"), p.Index + 1, p.Shells.Count, p.Normal.X, p.Normal.Y, p.Normal.Z)).ToList() ?? [];

    /// <summary>
    /// Ошибка настроек, при которой поле не сохраняется (текст для окна); null — всё в порядке: есть столбцы значений,
    /// компоненты не повторяются, допуски положительны, у величины есть цели, есть точки.
    /// </summary>
    public string? Validate()
    {
        var field = BuildField(_existingId);
        var issues = FemFieldAuthoring.Validate(field).Concat(FemFieldAuthoring.ValidateTargets(field, _ctx)).ToList();
        return issues.FirstOrDefault()?.Message;
    }

    /// <summary>Поле по текущим настройкам диалога (у открытого на правку — тот же Id).</summary>
    public FemField BuildField(int schemaId)
    {
        var (points, hasZ) = RawPoints();
        bool fromTable = _table != null;
        var def = new FemFieldDefinition
        {
            XHeader = fromTable ? Headers.ElementAtOrDefault(XColumn) ?? "X" : _existingDefinition?.XHeader ?? "X",
            YHeader = fromTable ? Headers.ElementAtOrDefault(YColumn) ?? "Y" : _existingDefinition?.YHeader ?? "Y",
            ZHeader = fromTable ? (ZColumn >= 0 ? Headers[ZColumn] : null) : _existingDefinition?.ZHeader,
            Columns = UsedColumns.Select(c => new FemFieldColumn(c.Header, c.Component, c.Unit.Label, c.Unit.ToSi)).ToList(),
            Alignment = Alignment,
            Targets = _targets,
            Settings = new FieldSettings(IsNodes ? FieldMethod.Match : Method, MaxDistance, PlaneTolerance,
                MatchTolerance: MatchTolerance),
        };
        return new FemField
        {
            Id = _existingId, SchemaId = schemaId, Name = Name, Quantity = Quantity, Definition = def,
            Points = points, HasZ = hasZ, SourceFile = SourceFile,
        };
    }
}
