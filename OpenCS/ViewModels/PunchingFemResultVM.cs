using System.Collections.ObjectModel;
using System.Globalization;
using System.Windows.Media;
using CScore;
using CScore.Fem;
using CScore.Sp63Punching;
using OpenCS.Tasks;
using OpenCS.Utilites;
using OpenCS.Views.Helpers;

namespace OpenCS.ViewModels;

/// <summary>Статусы узла продавливания: подпись и цвет (общие для таблиц и плана перекрытия).</summary>
public static class PunchingFemStatus
{
    public static string Text(string status) => status switch
    {
        "concrete" => Loc.S("PunchingFemStatus_concrete", "обеспечено по бетону"),
        "reinforcement" => Loc.S("PunchingFemStatus_reinforcement", "требуется армирование"),
        "not_ensured" => Loc.S("PunchingFemStatus_not_ensured", "прочность не обеспечена"),
        _ => Loc.S("PunchingFemStatus_no_check", "нет проверки"),
    };

    public static Color Color(string status, bool passed) => status switch
    {
        "concrete" => System.Windows.Media.Color.FromRgb(0x2D, 0x7A, 0x3E),
        "reinforcement" => passed ? System.Windows.Media.Color.FromRgb(0x1F, 0x6F, 0xB2) : System.Windows.Media.Color.FromRgb(0xD9, 0x8E, 0x04),
        "not_ensured" => System.Windows.Media.Color.FromRgb(0xC0, 0x39, 0x2B),
        _ => System.Windows.Media.Color.FromRgb(0x7F, 0x8C, 0x8D),
    };

    public static Brush Brush(string status, bool passed)
    {
        var b = new SolidColorBrush(Color(status, passed));
        b.Freeze();
        return b;
    }
}

/// <summary>Отметка плиты для плана.</summary>
public sealed record PunchingFemLevel(double Z, string Label);

/// <summary>Строка таблицы узлов.</summary>
public sealed class PunchingFemJointRowVM
{
    public PunchingFemJointRowVM(PunchingFemJointResult data)
    {
        Data = data;
        var c = CultureInfo.CurrentCulture;
        var gov = data.Rows.FirstOrDefault(r => r.Key == data.GoverningKey);
        Node = data.Node;
        Section = data.Section;
        Slab = data.SlabSection.Length > 0
            ? string.Format(c, "{0}; h0 = {1:0} " + Loc.S("UnitMm", "мм"), data.SlabSection, data.H0 * 1000)
            : "";
        Position = data.Edges switch
        {
            0 => Loc.S("PunchingPosition_inside", "внутри плиты"),
            1 => Loc.S("PunchingPosition_edge", "у края"),
            _ => Loc.S("PunchingPosition_corner", "в углу"),
        } + (data.Holes > 0 ? string.Format(c, Loc.S("PunchingFemHolesSuffix", ", отверстий {0}"), data.Holes) : "");
        IsChecked = data.Status != "no_check";
        K = IsChecked ? Variants.K(data.Ratio) : "—";
        KValue = IsChecked ? data.Ratio : -1;
        StatusText = PunchingFemStatus.Text(data.Status) + (data.NoCheckText is { Length: > 0 } t ? " — " + t : "");
        StatusShort = PunchingFemStatus.Text(data.Status);
        StatusBrush = PunchingFemStatus.Brush(data.Status, data.Passed);
        GoverningKey = data.GoverningKey;
        F = gov is null ? "" : gov.F.ToString("0.0", c);
        Mx = gov is null ? "" : gov.Mx.ToString("0.0", c);
        My = gov is null ? "" : gov.My.ToString("0.0", c);
        Method = gov is null ? "" : PunchingFemKeyRowVM.MethodText(gov.Method);
        Qsw = data.DesignState switch
        {
            null => "",
            "not_ensured" => Loc.S("PunchingFemQswNotEnsured", "не обеспечить"),
            "layout_ineffective" => Loc.S("PunchingFemQswLayout", "раскладка мимо"),
            _ when data.QswXReq is double qx => string.Format(c, "{0:0} / {1:0}", qx, data.QswYReq ?? 0),
            _ => (data.QswReq ?? 0).ToString("0", c),
        } + (data.DesignSufficient == true ? " ✔" : "");
        QswValue = (data.QswReq ?? Math.Max(data.QswXReq ?? 0, data.QswYReq ?? 0))
            + (data.DesignState is "not_ensured" or "layout_ineffective" ? 1e9 : 0);
        DesignText = data.DesignState is null ? "" : data.DesignState switch
        {
            "not_ensured" => string.Format(c, Loc.S("PunchingFemDesignNotEnsured", "Подбор по «{0}»: арматурой не обеспечить."), data.DesignKey),
            "layout_ineffective" => string.Format(c, Loc.S("PunchingFemDesignLayout", "Подбор по «{0}»: раскладка не пересекает определяющий контур."), data.DesignKey),
            _ => string.Format(c, Loc.S("PunchingFemDesignLine", "Подбор по «{0}»: {1} кН/м{2}{3}"), data.DesignKey,
                data.QswXReq is double x ? string.Format(c, "qsw,x ≥ {0:0.#}, qsw,y ≥ {1:0.#}", x, data.QswYReq ?? 0) : string.Format(c, "qsw ≥ {0:0.#}", data.QswReq ?? 0),
                data.BarDiameter is double dia ? string.Format(c, Loc.S("PunchingDesignBar", "; стержни Ø{0:0}"), dia * 1000) : "",
                data.DesignSufficient == true ? Loc.S("PunchingFemDesignEnough", "; заданной арматуры достаточно") : Loc.S("PunchingFemDesignNotEnough", "; заданной арматуры недостаточно")),
        };
        Warnings = data.Warnings.Count > 0 ? data.Warnings.Count.ToString(c) : "";
        WarningsText = string.Join("\n", data.Warnings.Select(w => "• " + w.Text));
        if (data.Lira is { } l)
        {
            LiraText = PunchingFemLiraText.Short(l.ErrorCode, l.Asw, l.Ksw);
            LiraHint = PunchingFemLiraText.Hint(l.Index, l.Perimeter, l.ConcreteClass, l.RebarClass, l.ErrorText);
            LiraValue = l.ErrorCode != 0 ? 1e9 : l.Asw > 0 ? 1e3 + l.Asw : l.Ksw;
        }
        if (data.LiraContour)
            ContourText = string.Format(c, Loc.S("PunchingFemJointCardContourLira", "Контур: ЛИРЫ, u = {0:0} мм"), (data.Lira?.Perimeter ?? 0) * 1000)
                // Ведущий пробел строки ресурса XAML отбрасывает — ставится здесь.
                + (data.UOpenCs is double u ? " " + string.Format(c, Loc.S("PunchingFemJointCardContourOpenCs", "(OpenCS: u = {0:0} мм)"), u * 1000) : "");
    }

    /// <summary>Основной контур ЛИРЫ в расчёте: «Контур: ЛИРЫ, u = … мм (OpenCS: u = … мм)»; пусто — контур OpenCS.</summary>
    public string ContourText { get; } = "";

    /// <summary>Результат ЛИРЫ по узлу кратко; пусто — ЛИРА узел не считала или *.asp не приложен.</summary>
    public string LiraText { get; } = "";
    public string? LiraHint { get; }
    /// <summary>Для сортировки: Ksw, затем узлы с Asw, затем с сообщениями; без ЛИРЫ — −1.</summary>
    public double LiraValue { get; } = -1;

    public PunchingFemJointResult Data { get; }
    public string Node { get; }
    public string Section { get; }
    public string Slab { get; }
    public string Position { get; }
    public bool IsChecked { get; }
    public string K { get; }
    public double KValue { get; }
    public string StatusText { get; }
    public string StatusShort { get; }
    public Brush StatusBrush { get; }
    public string GoverningKey { get; }
    public string F { get; }
    public string Mx { get; }
    public string My { get; }
    public string Method { get; }
    public string Warnings { get; }
    public string WarningsText { get; }
    /// <summary>Требуемое qsw узла (у креста — x / y), кН/м; ✔ — заданной хватает; пусто — бетона хватает.</summary>
    public string Qsw { get; }
    /// <summary>Для сортировки столбца qsw.</summary>
    public double QswValue { get; }
    /// <summary>Подбор узла строкой карточки.</summary>
    public string DesignText { get; }
}

/// <summary>Строка таблицы ключей узла.</summary>
public sealed class PunchingFemKeyRowVM
{
    /// <param name="jointKind">Вид узла (<see cref="PunchingFemJointResult.Kind"/>): у стен свои столбцы и моменты.</param>
    public PunchingFemKeyRowVM(PunchingFemRow row, bool governs, string jointKind = "bar")
    {
        bool wallEnd = jointKind == "wall_end", wallCorner = jointKind == "wall_corner";
        Row = row;
        var c = CultureInfo.CurrentCulture;
        Key = row.Key;
        Governs = governs;
        F = row.F.ToString("0.0", c);
        // У торца стены Mx — ΔM (½ΔM берёт ядро), My не участвует; у угла моменты не учитываются (СП 430, п. 6.3.8).
        Mx = wallCorner ? "—" : row.Mx.ToString("0.0", c);
        My = wallEnd || wallCorner ? "—" : row.My.ToString("0.0", c);
        Side = row.Side == "from_above" ? Loc.S("PunchingFemSideAbove", "сверху") : Loc.S("PunchingFemSideBelow", "снизу");
        Column = Num(row.ColumnAction);
        BaseLoad = Num(row.BaseLoad);
        Contour = Num(row.ContourForce);
        // У стены вместо невязки — сила граней основания поперёк стены (самопроверка: стена ≈ разрез + грани поперёк).
        Mismatch = wallEnd || wallCorner ? Num(row.WallExcluded)
            : row.ColumnAction is double a && row.ContourForce is double cf ? (a + (row.BaseLoad ?? 0) - cf).ToString("0.00", c) : "";
        Method = MethodText(row.Method);
        K = row.Error != null ? "—" : Variants.K(row.Ratio);
        KValue = row.Ratio;
        StatusText = PunchingFemStatus.Text(row.Status);
        StatusBrush = PunchingFemStatus.Brush(row.Status, row.Passed);
        Governing = row.Governing;
        Pylon = row.PylonMinus is double m && row.PylonPlus is double p
            ? string.Format(c, Loc.S("PunchingFemPylonParts", "торцы {0:0.0} и {1:0.0}, середина {2:0.0} кН; Кисп торцов {3}"),
                m, p, row.PylonMiddle ?? 0, row.PylonEndRatio is double k ? Variants.K(k) : "—")
            : "";
    }

    /// <summary>Пилон из пластин: силы торцов и середины по разрезу и Кисп торцов; пусто — не пилон.</summary>
    public string Pylon { get; }

    public static string MethodText(string method) => method == "contour"
        ? Loc.S("PunchingFemMethod_contour", "по разрезу")
        : Loc.S("PunchingFemMethod_column", "по колоннам");

    static string Num(double? v) => v is double x ? x.ToString("0.0", CultureInfo.CurrentCulture) : "—";

    public PunchingFemRow Row { get; }
    public string Key { get; }
    public bool Governs { get; }
    public string F { get; }
    public string Mx { get; }
    public string My { get; }
    public string Side { get; }
    public string Column { get; }
    public string BaseLoad { get; }
    public string Contour { get; }
    public string Mismatch { get; }
    public string Method { get; }
    public string K { get; }
    public double KValue { get; }
    public string StatusText { get; }
    public Brush StatusBrush { get; }
    public string Governing { get; }
}

/// <summary>
/// Окно результата проверки продавливания по МКЭ (<c>punching_check</c>): сводка, план перекрытия с узлами по
/// статусам, таблица узлов, ключи выбранного узла и полный расчёт выбранной строки (рисунок контуров, варианты, ход
/// вычислений — та же панель, что у одиночной задачи).
/// </summary>
public sealed class PunchingFemResultVM : ViewModelBase
{
    readonly IReadOnlyList<Material> _materials;
    readonly CalcType _calcType;
    PunchingFemJointRowVM? _selectedJoint;
    PunchingFemKeyRowVM? _selectedRow;

    public PunchingFemResultVM(CalcResult result, FemCheck? check, IEnumerable<Material> materials)
    {
        Result = result;
        Check = check;
        _materials = materials.ToList();
        _calcType = check?.CalcTypeOverride is { } s && Enum.TryParse<CalcType>(s, out var t) ? t : CalcType.C;
        Data = PunchingFemCheckData.Parse(result.DataJson) ?? new PunchingFemCheckData { Error = Loc.S("PunchingFemResultBroken", "Результат не читается.") };
        var c = CultureInfo.CurrentCulture;
        Title = check?.DisplayTag ?? Loc.S("PunchingFemTitle", "Продавливание по МКЭ");
        ErrorText = Data.Error ?? "";
        Summary = Data.Error != null ? "" : string.Format(c, Loc.S("PunchingFemSummary",
                "Схема «{0}», расчёт «{1}»: узлов {2}, не прошли {3}, без проверки {4}; загружений и сочетаний {5}; Кисп max = {6}"),
            Data.Schema, Data.Source, Data.TotalJoints, Data.FailedJoints, Data.NoCheckJoints, Data.Keys, Variants.K(Data.Ratio));
        Passed = Data.Passed;
        VerdictBrush = PunchingFemStatus.Brush(Data.Error != null ? "no_check" : Data.Passed ? "concrete" : Data.FailedJoints > 0 ? "not_ensured" : "no_check", Data.Passed);
        WarningsText = string.Join("\n", Data.Warnings.Select(w => "• " + w.Text));
        // Сначала проверенные по убыванию Кисп (не прошедшие — вверху), узлы без проверки — в конце.
        foreach (var j in Data.Joints.OrderBy(j => j.Status == "no_check").ThenByDescending(j => j.Ratio))
            Joints.Add(new PunchingFemJointRowVM(j));
        var zs = new List<double>();
        foreach (var z in Data.Slabs.Select(s => s.Z).Order())
            if (zs.Count == 0 || z - zs[^1] > Views.Helpers.PunchingFemPlanCanvas.LevelTol) zs.Add(z);
        Levels = zs.Select(z => new PunchingFemLevel(z, string.Format(c, Loc.S("PunchingFemLevel", "отм. {0:+0.000;−0.000;0.000}"), z))).ToList();
        SelectedJoint = Joints.FirstOrDefault();
        _level ??= Levels.FirstOrDefault();
    }

    /// <summary>Отметки плит с узлами.</summary>
    public IReadOnlyList<PunchingFemLevel> Levels { get; }
    public bool HasLevels => Levels.Count > 1;
    PunchingFemLevel? _level;
    /// <summary>Отметка плана.</summary>
    public PunchingFemLevel? SelectedLevel
    {
        get => _level;
        set { if (SetField(ref _level, value)) OnPropertyChanged(nameof(LevelZ)); }
    }
    public double? LevelZ => _level?.Z;

    void FollowLevel(PunchingFemJointRowVM? joint)
    {
        if (joint?.Data.Origin is not { Length: 3 } o || Levels.Count == 0) return;
        var near = Levels.MinBy(l => Math.Abs(l.Z - o[2]));
        if (near != null && near != _level) SelectedLevel = near;
    }

    public CalcResult Result { get; }
    public FemCheck? Check { get; }
    public PunchingFemCheckData Data { get; }
    public string Title { get; }
    public string Summary { get; }
    public string ErrorText { get; }
    public bool HasError => ErrorText.Length > 0;
    public bool Passed { get; }
    public Brush VerdictBrush { get; }
    public string VerdictText => HasError ? Loc.S("PunchingFemVerdictError", "Проверка не выполнена")
        : Passed ? Loc.S("PunchingFemVerdictOk", "Прочность на продавливание обеспечена во всех узлах")
        : Data.FailedJoints > 0 ? string.Format(CultureInfo.CurrentCulture, Loc.S("PunchingFemVerdictFail", "Прочность не обеспечена в узлах: {0}"), Data.FailedJoints)
        : Loc.S("PunchingFemVerdictNoCheck", "Есть узлы без проверки — см. причины в таблице");
    public string WarningsText { get; }
    public bool HasWarnings => WarningsText.Length > 0;
    /// <summary>Заголовок свёрнутых предупреждений проверки.</summary>
    public string WarningsHeader => string.Format(CultureInfo.CurrentCulture, Loc.S("PunchingFemWarningsHeader", "Предупреждения: {0}"), Data.Warnings.Count);

    public ObservableCollection<PunchingFemJointRowVM> Joints { get; } = [];
    public ObservableCollection<PunchingFemKeyRowVM> Rows { get; } = [];

    /// <summary>Есть узлы с результатом ЛИРЫ (*.asp был приложен при расчёте).</summary>
    public bool HasLira => Data.Joints.Any(j => j.Lira != null);
    /// <summary>Подпись к пунктиру ЛИРЫ на плане.</summary>
    public string LiraLegend => !HasLira ? "" : string.Format(CultureInfo.CurrentCulture,
        Loc.S("PunchingFemLiraLegend", "Пунктир — контуры продавливания ЛИРЫ ({0}): узлов с результатом ЛИРЫ {1} из {2}."),
        Data.LiraAspFile, Data.Joints.Count(j => j.Lira != null), Data.Joints.Count)
        + (Data.Joints.Count(j => j.LiraContour) is var n and > 0
            ? " " + string.Format(CultureInfo.CurrentCulture, Loc.S("PunchingFemLiraLegendCalc", "Сплошной — контур ЛИРЫ в расчёте (узлов {0})."), n)
            : "");

    public PunchingFemJointRowVM? SelectedJoint
    {
        get => _selectedJoint;
        set
        {
            if (!SetField(ref _selectedJoint, value)) return;
            Rows.Clear();
            if (value != null)
                foreach (var r in value.Data.Rows.OrderByDescending(r => r.Ratio))
                    Rows.Add(new PunchingFemKeyRowVM(r, r.Key == value.Data.GoverningKey, value.Data.Kind));
            OnPropertyChanged(nameof(SelectedJointNode));
            OnPropertyChanged(nameof(JointHeader));
            OnPropertyChanged(nameof(JointCard));
            OnPropertyChanged(nameof(JointTabHeader));
            OnPropertyChanged(nameof(HasSelectedJoint));
            foreach (var n in new[] { nameof(IsWallJoint), nameof(RowsColumnHeader), nameof(RowsMismatchHeader), nameof(RowsMxHeader), nameof(RowsHint) })
                OnPropertyChanged(n);
            if (Levels != null) FollowLevel(value);
            SelectedRow = Rows.FirstOrDefault();
            if (Rows.Count == 0) { Detail = null; OnPropertyChanged(nameof(Detail)); }
        }
    }

    /// <summary>Тег выбранного узла (выделение на плане).</summary>
    public string? SelectedJointNode => _selectedJoint?.Node;

    public string JointHeader => _selectedJoint == null ? ""
        : string.Format(CultureInfo.CurrentCulture, Loc.S("PunchingFemJointHeader", "Узел {0}: {1}"), _selectedJoint.Node, _selectedJoint.StatusText)
          + (_selectedJoint.WarningsText.Length > 0 ? "\n" + _selectedJoint.WarningsText : "");

    public bool HasSelectedJoint => _selectedJoint != null;

    /// <summary>Выбран узел стены (торец или угол): у таблицы ключей свои столбцы, «Торцы пилона» скрыты.</summary>
    public bool IsWallJoint => _selectedJoint?.Data.Kind is "wall_end" or "wall_corner";
    /// <summary>Столбец действия опоры: «Колонны» или «Стена в основании».</summary>
    public string RowsColumnHeader => IsWallJoint ? Loc.S("PunchingFemColWallBase", "Стена в основании, кН") : Loc.S("PunchingFemColColumn", "Колонны, кН");
    /// <summary>Столбец невязки; у стены — сила граней основания поперёк стены.</summary>
    public string RowsMismatchHeader => IsWallJoint ? Loc.S("PunchingFemColWallFaces", "Грани поперёк стены, кН") : Loc.S("PunchingFemColMismatch", "Невязка, кН");
    /// <summary>Столбец Mx; у торца стены — ΔM.</summary>
    public string RowsMxHeader => _selectedJoint?.Data.Kind == "wall_end" ? "ΔM" : "Mx";
    /// <summary>Подсказка к таблице ключей.</summary>
    public string RowsHint => IsWallJoint
        ? Loc.S("PunchingFemRowsHintWall", "«Стена в основании» — действие стены на плиту, «Разрез» — сила продавливания по граням основания вдоль контура, «Грани поперёк стены» — сила на гранях основания, пересекающих стену (в силу продавливания не входит); стена ≈ разрез + грани поперёк. У торца Mx — разность моментов плиты ΔM по сторонам стены (в расчёт ½ΔM), My не учитывается; у угла моменты не учитываются (СП 430, п. 6.3.8). Ниже — полный расчёт строки.")
        : Loc.S("PunchingFemRowsHint", "");

    /// <summary>Вкладка расчёта узла: «Узел 47460».</summary>
    public string JointTabHeader => _selectedJoint == null ? Loc.S("PunchingFemJointTabEmpty", "Расчёт узла")
        : string.Format(CultureInfo.CurrentCulture, Loc.S("PunchingFemJointTab", "Узел {0}"), _selectedJoint.Node);

    /// <summary>Карточка узла на вкладке плана: сечения, положение, определяющий ключ и его усилия.</summary>
    public string JointCard
    {
        get
        {
            if (_selectedJoint is not { } j) return "";
            var c = CultureInfo.CurrentCulture;
            var lines = new List<string>();
            if (j.Section.Length > 0) lines.Add(j.Section);
            lines.Add(j.Slab.Length > 0 ? j.Slab + "; " + j.Position : j.Position);
            if (j.IsChecked)
                lines.Add(string.Format(c, Loc.S("PunchingFemJointCardKey", "Кисп = {0} по «{1}»: F = {2} кН, Mx = {3}, My = {4} кН·м"),
                    j.K, j.GoverningKey, j.F, j.Mx, j.My));
            if (j.DesignText.Length > 0) lines.Add(j.DesignText);
            if (j.LiraText.Length > 0)
                lines.Add(string.Format(c, Loc.S("PunchingFemJointCardLira", "ЛИРА: {0}"), j.LiraText)
                    + (j.Data.Lira?.ErrorText is { } et ? " — " + et : ""));
            if (j.ContourText.Length > 0) lines.Add(j.ContourText);
            return string.Join("\n", lines);
        }
    }

    public PunchingFemKeyRowVM? SelectedRow
    {
        get => _selectedRow;
        set
        {
            if (!SetField(ref _selectedRow, value)) return;
            Detail = BuildDetail();
            OnPropertyChanged(nameof(Detail));
        }
    }

    /// <summary>Полный расчёт выбранной строки (одиночная задача с трассировкой); null — строки нет.</summary>
    public PunchingResultVM? Detail { get; private set; }

    /// <summary>Параметры одиночной задачи выбранной строки; null — нет.</summary>
    public PunchingTaskParams? SelectedRowParams =>
        _selectedJoint != null && _selectedRow != null ? PunchingFemCheckRunner.RowParams(_selectedJoint.Data, _selectedRow.Row) : null;

    PunchingResultVM? BuildDetail()
    {
        if (SelectedRowParams is not { } p) return null;
        var task = new CalcTask
        {
            Kind = PunchingRunner.Kind, CalcType = _calcType,
            Tag = string.Format(CultureInfo.CurrentCulture, "{0} — {1} {2}, {3}", Title, Loc.S("PunchingFemNode", "узел"), _selectedJoint!.Node, _selectedRow!.Key),
        };
        try
        {
            var errors = p.Validate();
            var strengths = PunchingRunner.ResolveStrengths(p, _materials, _calcType, out var materialErrors);
            errors.AddRange(materialErrors);
            if (errors.Count > 0)
                return new PunchingResultVM(new CalcResult
                {
                    TaskKind = task.Kind, Status = "error",
                    DataJson = System.Text.Json.JsonSerializer.Serialize(new
                    {
                        error = string.Join("; ", errors.Select(code => Loc.S("PunchingErr_" + code, code))), errors, traceVersion = 1,
                    }),
                }, task);
            var input = p.ToInput(strengths.Rbt, strengths.Rsw);
            var r = PunchingChecker.Check(input);
            return new PunchingResultVM(new CalcResult
            {
                TaskKind = task.Kind, TaskTag = task.Tag, Status = r.Passed ? "ok" : "not_passed",
                DataJson = PunchingRunner.Serialize(task, p, strengths, input, r),
            }, task);
        }
        catch (Exception ex)
        {
            return new PunchingResultVM(new CalcResult { TaskKind = task.Kind, Status = "error",
                DataJson = System.Text.Json.JsonSerializer.Serialize(new { error = ex.Message }) }, task);
        }
    }

    bool SetField<T>(ref T field, T value, [System.Runtime.CompilerServices.CallerMemberName] string? name = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return false;
        field = value;
        OnPropertyChanged(name);
        return true;
    }

    /// <summary>Выбор узла по тегу (щелчок на плане).</summary>
    public void SelectNode(string node)
    {
        if (Joints.FirstOrDefault(j => j.Node == node) is { } j) SelectedJoint = j;
    }
}
