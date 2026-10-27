using System.Globalization;
using System.Text;
using CScore;
using CScore.History;
using CScore.Hysteresis;
using OpenCS.LoadHistory;
using OpenCS.Services;
using OpenCS.Utilites;

namespace OpenCS.ViewModels;

/// <summary>Строка таблицы шагов истории нагружения.</summary>
public sealed class LoadHistoryStepRowVM
{
    /// <summary>Индекс шага в Steps / StepsNoHistory.</summary>
    public int Index { get; init; }
    /// <summary>Шаг пути «без истории».</summary>
    public bool NoHistory { get; init; }
    public string Stage { get; init; } = "";
    public string Kind { get; init; } = "";
    public int Step { get; init; }
    public int Parts { get; init; }
    public double N { get; init; }
    public double Mx { get; init; }
    public double My { get; init; }
    public double C { get; init; }
    public double Mu { get; init; }
    public bool Converged { get; init; }
    /// <summary>Наибольшее по частям использование бетона.</summary>
    public double EtaB { get; init; }
    /// <summary>Наибольшее по частям использование арматуры.</summary>
    public double EtaS { get; init; }
    /// <summary>Наибольшая по частям остаточная деформация бетона (0 без гистерезиса).</summary>
    public double ResB { get; init; }
    /// <summary>Наибольшая по частям остаточная деформация арматуры (0 без гистерезиса).</summary>
    public double ResS { get; init; }
    /// <summary>Последний шаг этапа.</summary>
    public bool IsStageEnd { get; init; }
    /// <summary>Первый шаг этапа.</summary>
    public bool IsStageStart { get; init; }
    /// <summary>Шаг не сошёлся или с предупреждениями.</summary>
    public bool HasProblem { get; init; }
    public string Warnings { get; init; } = "";
    /// <summary>Точка предела по деформациям (M_lim).</summary>
    public bool LimitPoint { get; init; }
    /// <summary>Шаг решён без растяжения бетона (продолжение несошедшегося шага).</summary>
    public bool WithoutConcreteTension { get; init; }
    /// <summary>Плоскость деформаций шага.</summary>
    public Kurvature K { get; init; }
}

/// <summary>
/// Окно результата задачи load_history: сводка, таблица шагов (с историей / без), кривые «момент–кривизна»
/// и поля σ/ε по выбранному шагу (восстанавливаются из K шага и плоскостей включения частей; при гистерезисе —
/// воспроизведением пути до шага, <see cref="LoadHistoryReplay"/>).
/// </summary>
public sealed class LoadHistoryResultVM : ViewModelBase
{
    readonly StagedSection? _section;
    readonly CalcType _calc;
    readonly CalcSettings _settings;
    readonly IFileDialogService? _fileDialogService;
    readonly string _titleSuffix;
    bool _showNoHistory, _fieldsRelative = true;
    LoadHistoryStepRowVM? _selectedRow;
    SectionPlotVM? _stressPlot, _strainPlot;

    /// <param name="fileDialogService">Для экспорта эпюр разреза; null — инструмент разреза недоступен.</param>
    public LoadHistoryResultVM(CalcResult result, StagedSection? section, CalcType calc, CalcSettings settings,
        IFileDialogService? fileDialogService = null)
    {
        _section = section;
        _calc = calc;
        _settings = settings;
        _fileDialogService = fileDialogService;
        _titleSuffix = section is null ? result.TaskTag : $"{result.TaskTag} — {section.Tag}";
        Data = LoadHistoryResultData.Parse(result.DataJson);
        Curves = LoadHistoryCurveBuilder.Build(Data);
        Rows = BuildRows(Data.Steps, noHistory: false);
        RowsNoHistory = Data.StepsNoHistory is null ? [] : BuildRows(Data.StepsNoHistory, noHistory: true);
        HysteresisText = BuildHysteresisText();
        SummaryText = BuildSummary();
        RolesNote = BuildRolesNote();
        _selectedRow = Rows.LastOrDefault();
        RefreshFields();
    }

    public LoadHistoryResultData Data { get; }
    public LoadHistoryCurves Curves { get; }
    public IReadOnlyList<LoadHistoryStepRowVM> Rows { get; }
    public IReadOnlyList<LoadHistoryStepRowVM> RowsNoHistory { get; }

    /// <summary>Есть путь «без истории».</summary>
    public bool HasNoHistory => Data.StepsNoHistory != null;

    /// <summary>Сечение задачи найдено — поля доступны.</summary>
    public bool HasSection => _section != null;

    /// <summary>Таблица показывает путь «без истории».</summary>
    public bool ShowNoHistory
    {
        get => _showNoHistory;
        set
        {
            if (_showNoHistory == value) return;
            _showNoHistory = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(CurrentRows));
            OnPropertyChanged(nameof(MaxIndex));
            OnPropertyChanged(nameof(StageTicks));
            OnPropertyChanged(nameof(StepPosition));
            OnPropertyChanged(nameof(SelectedStepText));
        }
    }

    /// <summary>
    /// Позиция ползунка вкладки «Поля» — индекс выбранного шага в <see cref="CurrentRows"/>; дробное значение
    /// округляется до шага (прилипание к делениям у ползунка с отметками этапов работало бы только по ним).
    /// </summary>
    public double StepPosition
    {
        get => _selectedRow is { } r && r.NoHistory == _showNoHistory ? r.Index : 0;
        set
        {
            SelectStep(_showNoHistory, (int)Math.Round(value));
            OnPropertyChanged(); // вернуть ползунок точно на шаг
        }
    }

    /// <summary>Наибольший индекс шага текущего пути.</summary>
    public int MaxIndex => Math.Max(0, CurrentRows.Count - 1);

    /// <summary>Отметки ползунка — начала этапов текущего пути.</summary>
    public System.Windows.Media.DoubleCollection StageTicks =>
        new(CurrentRows.Where(r => r.IsStageStart).Select(r => (double)r.Index));

    /// <summary>Подпись выбранного шага рядом с ползунком.</summary>
    public string SelectedStepText
    {
        get
        {
            if (_selectedRow is not { } r) return "";
            var rows = r.NoHistory ? RowsNoHistory : Rows;
            string text = string.Format(CultureInfo.CurrentCulture, Loc.S("LoadHistory_SliderStep"),
                r.Index + 1, rows.Count, r.Stage, r.Step, r.Mu);
            return r.NoHistory ? $"{text} ({Loc.S("LoadHistory_WithoutHistory")})" : text;
        }
    }

    /// <summary>Строки таблицы выбранного пути.</summary>
    public IReadOnlyList<LoadHistoryStepRowVM> CurrentRows => _showNoHistory ? RowsNoHistory : Rows;

    /// <summary>Выбранный шаг; его поля — на вкладке «Поля».</summary>
    public LoadHistoryStepRowVM? SelectedRow
    {
        get => _selectedRow;
        set
        {
            if (ReferenceEquals(_selectedRow, value)) return;
            _selectedRow = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(StepPosition));
            OnPropertyChanged(nameof(SelectedStepText));
            RefreshFields();
        }
    }

    /// <summary>Деформации относительно включения части (иначе — полные).</summary>
    public bool FieldsRelative
    {
        get => _fieldsRelative;
        set
        {
            if (_fieldsRelative == value) return;
            _fieldsRelative = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(FieldsTotal));
            RefreshFields();
        }
    }

    /// <summary>Обратный к <see cref="FieldsRelative"/> флаг для переключателя.</summary>
    public bool FieldsTotal { get => !_fieldsRelative; set => FieldsRelative = !value; }

    public SectionPlotVM? StressPlot { get => _stressPlot; private set { _stressPlot = value; OnPropertyChanged(); } }
    public SectionPlotVM? StrainPlot { get => _strainPlot; private set { _strainPlot = value; OnPropertyChanged(); } }

    public string SummaryText { get; }
    public string? RolesNote { get; }

    /// <summary>Строка модели материала при гистерезисе; null — нелинейно-упругий материал.</summary>
    public string? HysteresisText { get; }

    /// <summary>Показывать оговорку о разгрузке по кривой нагружения (только без гистерезиса).</summary>
    public bool ShowUnloadNote => HysteresisText is null;

    /// <summary>Выбирает шаг пути по индексу (щелчок по кривой).</summary>
    public void SelectStep(bool noHistory, int index)
    {
        var rows = noHistory ? RowsNoHistory : Rows;
        if (index < 0 || index >= rows.Count) return;
        ShowNoHistory = noHistory;
        SelectedRow = rows[index];
    }

    IReadOnlyList<LoadHistoryStepRowVM> BuildRows(IReadOnlyList<HistoryStepDto> steps, bool noHistory)
    {
        var rows = new List<LoadHistoryStepRowVM>(steps.Count);
        for (int i = 0; i < steps.Count; i++)
        {
            var s = steps[i];
            bool failure = s.Kind == "failure";
            rows.Add(new LoadHistoryStepRowVM
            {
                Index = i, NoHistory = noHistory,
                Stage = failure ? Loc.S("LoadHistory_KindFailure")
                    : s.Stage < Data.StageTags.Count ? Data.StageTags[s.Stage] : (s.Stage + 1).ToString(CultureInfo.InvariantCulture),
                Kind = Loc.S(failure ? "LoadHistory_KindFailure" : "LoadHistory_KindLoad"),
                Step = s.Step, Parts = s.Parts,
                N = s.N, Mx = s.Mx, My = s.My, C = s.C, Mu = s.Mu,
                Converged = s.Converged,
                EtaB = s.PartStates.Count == 0 ? 0 : s.PartStates.Max(p => p.EtaB),
                EtaS = s.PartStates.Count == 0 ? 0 : s.PartStates.Max(p => p.EtaS),
                ResB = s.PartStates.Count == 0 ? 0 : s.PartStates.Max(p => p.EbRes),
                ResS = s.PartStates.Count == 0 ? 0 : s.PartStates.Max(p => p.EsRes),
                IsStageStart = i == 0 || steps[i - 1].Stage != s.Stage,
                IsStageEnd = i == steps.Count - 1 || steps[i + 1].Stage != s.Stage,
                HasProblem = !s.Converged || s.Warnings.Count > 0,
                Warnings = string.Join("; ", s.Warnings.Select(w => Loc.S("LoadHistory_Warn_" + w, w))),
                LimitPoint = s.LimitPoint,
                WithoutConcreteTension = s.Warnings.Contains("without_concrete_tension"),
                K = s.K,
            });
        }
        return rows;
    }

    string BuildSummary()
    {
        if (Data.Error != null) return Data.Error;
        var sb = new StringBuilder();
        if (HysteresisText != null) sb.AppendLine(HysteresisText);
        string F(double? v) => v is double d ? d.ToString("F2", CultureInfo.CurrentCulture) : "—";
        string Pair(double? with, double? without) => Data.Without is null
            ? F(with)
            : $"{F(with)} ({Loc.S("LoadHistory_WithHistory")}) / {F(without)} ({Loc.S("LoadHistory_WithoutHistory")})";

        sb.AppendLine($"{Loc.S("LoadHistory_MLim")}: {Pair(Data.With.MLim, Data.Without?.MLim)}");
        sb.AppendLine($"{Loc.S("LoadHistory_MPeak")}: {Pair(Data.With.MPeak, Data.Without?.MPeak)}");
        if (Data.Without is { } w)
        {
            string Ratio(double? a, double? b) => a is double x && b is double y && Math.Abs(y) > 1e-12
                ? (x / y).ToString("F3", CultureInfo.CurrentCulture) : "—";
            sb.AppendLine($"{Loc.S("LoadHistory_Ratio")}: M_lim {Ratio(Data.With.MLim, w.MLim)}, M_peak {Ratio(Data.With.MPeak, w.MPeak)}");
        }
        if (Data.With.GoverningPart is int part)
        {
            string material = Data.With.GoverningMaterial == "rebar" ? Loc.S("LoadHistory_Rebar") : Loc.S("LoadHistory_Concrete");
            string tag = part < Data.PartTags.Count ? $"{part + 1} «{Data.PartTags[part]}»" : (part + 1).ToString(CultureInfo.InvariantCulture);
            sb.AppendLine(string.Format(CultureInfo.CurrentCulture, Loc.S("LoadHistory_Governing"),
                tag, material, Data.With.GoverningX ?? 0, Data.With.GoverningY ?? 0));
        }
        sb.Append($"{Loc.S("LoadHistory_StopReason")}: {StopReasonText(Data.With.StopReason)}");
        if (Data.Without is { } wo)
            sb.Append($" / {StopReasonText(wo.StopReason)} ({Loc.S("LoadHistory_WithoutHistory")})");
        return sb.ToString();
    }

    string? BuildHysteresisText()
    {
        if (Data.Hysteresis is not { } h) return null;
        var c = CultureInfo.CurrentCulture;
        string steel = h.Steel == SteelHysteresisModel.Bilinear
            ? Loc.S("LoadHistory_SteelBilinear")
            : string.Format(c, "{0} (R0 = {1:G4}; cR1 = {2:G4}; cR2 = {3:G4})", Loc.S("LoadHistory_SteelMP"), h.R0, h.CR1, h.CR2);
        string concrete = Loc.S(h.Concrete == ConcreteHysteresisModel.NonlinearElastic
            ? "LoadHistory_ConcreteElastic" : "LoadHistory_ConcreteKJ");
        return string.Format(c, Loc.S("LoadHistory_HysteresisSummary"), concrete, steel);
    }

    static string StopReasonText(string code) => Loc.S("LoadHistory_StopReason_" + code, code);

    string? BuildRolesNote()
    {
        if (Data.NonStandardRoles.Count == 0) return null;
        string Role(PartLimitRole r) => Loc.S(r switch
        {
            PartLimitRole.Ignore => "LoadHistory_RoleIgnore",
            PartLimitRole.Fails => "LoadHistory_RoleFails",
            _ => "LoadHistory_RoleLimits",
        });
        var parts = Data.NonStandardRoles.Select(r =>
        {
            string tag = r.PartIndex < Data.PartTags.Count ? $" «{Data.PartTags[r.PartIndex]}»" : "";
            return $"{r.PartIndex + 1}{tag}: {Loc.S("LoadHistory_Concrete")} — {Role(r.Concrete)}, {Loc.S("LoadHistory_Rebar")} — {Role(r.Rebar)}";
        });
        return Loc.S("LoadHistory_RolesNote") + ": " + string.Join("; ", parts);
    }

    /// <summary>Восстанавливает поля σ/ε выбранного шага по копии сечения.</summary>
    void RefreshFields()
    {
        var row = _selectedRow;
        if (_section == null || row == null || Data.Error != null)
        {
            StressPlot = null;
            StrainPlot = null;
            return;
        }
        bool hysteresis = Data.Hysteresis is not null;
        bool ten = (hysteresis ? Data.ConcreteTension : _settings.ResolveConcreteTension(_calc)) && !row.WithoutConcreteTension;
        bool fibers = Data.StrainMode == "fibers";

        // При гистерезисе σ зависят от пути — состояние волокон восстанавливается воспроизведением шагов 0…i.
        var copy = hysteresis
            ? LoadHistoryReplay.Section(_section, Data, _calc, row.NoHistory, row.Index)
            : PrepareCopy(row, fibers);
        copy.SetEps(row.K, _calc, ten);
        var stress = new SectionPlotVM(copy, row.K, _calc, SectionPlotMode.Stress, _settings, ten);
        var cut = Cut(copy, row, ten);
        stress.CutVM = cut;
        StressPlot = stress;

        if (_fieldsRelative)
            StrainPlot = new SectionPlotVM(copy, row.K, _calc, SectionPlotMode.Strain, _settings, ten) { CutVM = cut };
        else
        {
            // Полные деформации: те же включённые части без плоскостей включения и начальных деформаций.
            var plain = (StagedSection)_section.CloneForCalc();
            plain.ResetHistory();
            var total = new CrossSection { Areas = plain.Parts.Take(Math.Max(1, row.Parts)).SelectMany(p => p.Areas).ToList() };
            total.SetEps(row.K, _calc, ten);
            StrainPlot = new SectionPlotVM(total, row.K, _calc, SectionPlotMode.Strain, _settings, ten)
                { CutVM = Cut(total, row, ten) };
        }
    }

    /// <summary>Инструмент разреза по сечению шага (эпюры σ/ε вдоль линии на карте).</summary>
    SectionCutVM? Cut(CrossSection section, LoadHistoryStepRowVM row, bool ten) =>
        _fileDialogService is null ? null
            : new SectionCutVM(section, row.K, _calc, _fileDialogService, ten)
                { WindowTitleSuffix = $"{_titleSuffix} ({row.Stage}, {row.Step})" };

    StagedSection PrepareCopy(LoadHistoryStepRowVM row, bool fibers)
    {
        var copy = (StagedSection)_section!.CloneForCalc();
        copy.ResetHistory();
        if (fibers)
        {
            copy.StrainMode = StageStrainMode.Fibers;
            copy.PrepareFiberMode();
        }
        if (row.NoHistory)
            copy.ActivateAll();
        else
            for (int k = 1; k < row.Parts && k < copy.Parts.Count; k++)
            {
                var p = k < Data.ActivationPlanes.Count ? Data.ActivationPlanes[k] : [0, 0, 0];
                copy.ActivatePart(k, new Kurvature { e0 = p[0], ky = p[1], kz = p[2] });
            }
        return copy;
    }
}
