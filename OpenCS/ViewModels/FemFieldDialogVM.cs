using System.Collections.ObjectModel;
using System.Globalization;
using System.IO;
using CScore.Fem;
using CScore.Fem.Fields;
using OpenCS.Utilites;

namespace OpenCS.ViewModels;

/// <summary>Строка привязки столбца значений: компонента величины и единица ввода.</summary>
public sealed class FemFieldColumnRow : ViewModelBase
{
    string _component;
    FemFieldUnit _unit;

    public FemFieldColumnRow(int columnIndex, string header, string component, IReadOnlyList<FemFieldUnit> units, FemFieldUnit unit)
    {
        ColumnIndex = columnIndex; Header = header; _component = component; Units = units; _unit = unit;
    }

    public int ColumnIndex { get; }
    public string Header { get; }
    public IReadOnlyList<FemFieldUnit> Units { get; internal set; }
    public string Component { get => _component; set { _component = value; OnPropertyChanged(); } }
    public FemFieldUnit Unit { get => _unit; set { _unit = value; OnPropertyChanged(); } }
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
        }
    }

    // Поле, открытое на правку: его точки и столбцы действуют, пока не загружена новая таблица.
    IReadOnlyList<FieldPoint>? _existingPoints;
    readonly FemFieldDefinition? _existingDefinition;
    readonly bool _existingHasZ;

    public string Name { get; set; } = "";
    public string? SourceFile { get; private set; }
    public IReadOnlyList<string> Headers { get; private set; } = [];
    public int XColumn { get; set; }
    public int YColumn { get; set; } = 1;
    public int ZColumn { get; set; } = -1;
    public double LengthScale { get; set; } = 1;
    public string Quantity { get; set; } = FemFieldQuantities.FoundationC1;
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

        var valueCols = Enumerable.Range(0, Headers.Count)
            .Where(i => i != XColumn && i != YColumn && i != ZColumn && _table.ColumnHasNumbers(i)).ToList();
        bool spring = valueCols.Any(i => SpringComponent(Headers[i]) != null);
        Quantity = spring ? FemFieldQuantities.Spring : FemFieldQuantities.FoundationC1;
        var q = FemFieldQuantities.Get(Quantity);
        Columns.Clear();
        foreach (int i in valueCols)
        {
            string comp = SpringComponent(Headers[i]) ?? q.Components[0];
            var units = FemFieldQuantities.Units(Quantity, comp, _tonToKn);
            Columns.Add(new FemFieldColumnRow(i, Headers[i], comp, units, GuessUnit(Headers[i], units)));
        }
        if (IsNodes) Method = FieldMethod.Match;
        else
        {
            var defaults = FieldDefaults.For(TargetSet().Shells);
            (MaxDistance, PlaneTolerance) = (defaults.MaxDistance, defaults.PlaneTolerance);
            if (Method == FieldMethod.Match) Method = FieldMethod.Interpolate;
        }
        OnPropertyChanged(string.Empty);
    }

    static string? SpringComponent(string header)
    {
        string h = header.TrimStart().ToLowerInvariant();
        foreach (var (p, c) in new[] { ("rz", "kz"), ("kz", "kz"), ("rx", "kx"), ("kx", "kx"), ("ry", "ky"), ("ky", "ky") })
            if (h.StartsWith(p, StringComparison.Ordinal)) return c;
        return null;
    }

    static FemFieldUnit GuessUnit(string header, IReadOnlyList<FemFieldUnit> units)
    {
        string h = header.ToLowerInvariant().Replace(" ", "");
        string? label = h.Contains("тс/см") ? "тс/см"
            : h.Contains("тс/м3") || h.Contains("тс/м³") || h.Contains("т/м3") || h.Contains("t_m3") ? "тс/м³"
            : h.Contains("кн/м3") || h.Contains("кн/м³") ? "кН/м³"
            : h.Contains("тс/м") ? "тс/м"
            : null;
        return units.FirstOrDefault(u => u.Label == label) ?? units[0];
    }

    FieldAlignment Alignment => new(LengthScale, MirrorX, MirrorY, AngleDeg, Dx, Dy, Dz);

    FemFieldTargetSet TargetSet() => FemFieldEvaluator.ResolveTargets(
        new FemField { Quantity = Quantity, Definition = new FemFieldDefinition { Targets = _targets } }, _ctx);

    (List<FieldPoint> Points, bool HasZ) RawPoints()
    {
        if (_table == null) return ((_existingPoints ?? []).ToList(), _existingHasZ);
        var (pts, _) = _table.Select(XColumn, YColumn, ZColumn >= 0 ? ZColumn : null, Columns.Select(c => c.ColumnIndex).ToList());
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
        var t = Evaluation.Targets;
        Summary = string.Format(CultureInfo.CurrentCulture, Loc.S("FemFieldPreviewSummary"),
            t.Count, t.Count(x => x.How == FieldHow.Interpolated), t.Count(x => x.How == FieldHow.Nearest),
            t.Count(x => x.How == FieldHow.Idw), t.Count(x => x.How == FieldHow.Matched), t.Count(x => x.How == FieldHow.None),
            Evaluation.PointCount, Evaluation.PointsUnused);
        OnPropertyChanged(nameof(Evaluation));
        OnPropertyChanged(nameof(Summary));
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
            Columns = fromTable || _existingDefinition == null
                ? Columns.Select(c => new FemFieldColumn(c.Header, c.Component, c.Unit.Label, c.Unit.ToSi)).ToList()
                : _existingDefinition.Columns,
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
