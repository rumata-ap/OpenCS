using System.ComponentModel;
using System.Globalization;
using CScore.Fem;
using CScore.Fem.Editing;
using OpenCS.Utilites;

namespace OpenCS.ViewModels;

/// <summary>Пункт списка в ячейке таблицы: ключ домена и подпись.</summary>
public sealed record FemLoadsChoice(string Key, string Label);

/// <summary>Строка таблицы вкладки «Загружения»: перечитывает значения из домена без пересборки таблицы.</summary>
public abstract class FemLoadsRow : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;
    /// <summary>Доменный объект строки (по ссылке сверяется состав таблицы).</summary>
    public abstract object Source { get; }
    /// <summary>Значения в ячейках можно править (импортные нагрузки — только чтение и удаление).</summary>
    public virtual bool IsEditable => true;
    public void Refresh() => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(null));
    protected void Notify(string name) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}

/// <summary>Загружение в таблице: правка ячеек — <see cref="EditLoadCaseCommand"/>, одна отмена на ячейку.</summary>
public sealed class FemLoadCaseRow(FemLoadsTabVM owner, FemLoadCase loadCase, int number) : FemLoadsRow
{
    public FemLoadCase LoadCase { get; } = loadCase;
    public override object Source => LoadCase;
    public int Number { get; } = number;
    public bool IsActive => ReferenceEquals(owner.Editor.SelectedLoadCase, LoadCase);
    public string ActiveMark => IsActive ? "●" : "";
    public string? OriginText => LoadCase.Origin == FemLoadOrigin.Manual ? null : FemMeshNodeRow.DescribeOrigin(LoadCase.Origin);

    public string Tag
    {
        get => LoadCase.Tag;
        set => owner.EditLoadCase(this, v => v.Tag = value ?? "");
    }
    public string Sp20Type
    {
        get => LoadCase.Sp20Type;
        set { if (!string.IsNullOrEmpty(value)) owner.EditLoadCase(this, v => v.Sp20Type = value); }
    }
    public string Group
    {
        get => LoadCase.Sp20Group ?? "";
        set => owner.EditLoadCase(this, v => v.Sp20Group = string.IsNullOrWhiteSpace(value) ? null : value.Trim());
    }
    public string GammaFUnfav { get => FemLoadsFormat.Optional(LoadCase.GammaFUnfav); set => SetOptional(value, (v, x) => v.GammaFUnfav = x); }

    /// <summary>γf при уменьшении веса (п. 7.3 СП 20) — только у постоянных; у прочих ячейка пуста и не правится.</summary>
    public string GammaFFav
    {
        get => IsPermanent ? FemLoadsFormat.Optional(LoadCase.GammaFFav) : "";
        set { if (IsPermanent) SetOptional(value, (v, x) => v.GammaFFav = x); }
    }

    public bool IsPermanent => LoadCase.Sp20Type == "permanent";
    public bool IsShortTerm => LoadCase.Sp20Type == "short_term";

    /// <summary>
    /// Доля длительной части (пониженное значение, СП 20) — правится у кратковременных (0…1, пусто — не задана);
    /// постоянные и длительные входят в CL/NL целиком («1»), особые не входят («—»).
    /// </summary>
    public string LongTermShare
    {
        get => IsShortTerm ? FemLoadsFormat.Optional(LoadCase.LongTermShare)
            : LoadCase.Sp20Type == "accidental" ? "—" : FemLoadsFormat.Number(1);
        set
        {
            if (!IsShortTerm) { Refresh(); return; }
            if (!FemLoadsFormat.TryOptional(value, out var share, allowNegative: false) || share > 1)
            {
                owner.Reject(Loc.S("Sp20AttrBadShare"), this);
                return;
            }
            owner.EditLoadCase(this, v => v.LongTermShare = share);
        }
    }

    /// <summary>Множитель собственного веса; пусто — без собственного веса.</summary>
    public string SelfWeight
    {
        get => FemLoadsFormat.Optional(LoadCase.SelfWeightFactor);
        set
        {
            if (!FemLoadsFormat.TryOptional(value, out var factor, allowNegative: true) || factor == 0)
            {
                owner.Reject(Loc.S("FemSelfWeightFactorInvalid"), this);
                return;
            }
            owner.EditLoadCase(this, v => v.SelfWeightFactor = factor);
        }
    }

    void SetOptional(string value, Action<FemLoadCase, double?> apply)
    {
        if (!FemLoadsFormat.TryOptional(value, out var x, allowNegative: false))
        {
            owner.Reject(Loc.S("FemLoadCaseDialogBadNumber"), this);
            return;
        }
        owner.EditLoadCase(this, v => apply(v, x));
    }
}

/// <summary>Узловая нагрузка КонЭ: Fx…Fz, кН; Mx…Mz, кН·м.</summary>
public sealed class FemNodeLoadRow(FemLoadsTabVM owner, FemNodeLoad load, string nodeTag) : FemLoadsRow
{
    public FemNodeLoad Load { get; } = load;
    public override object Source => Load;
    public string NodeTag { get; } = nodeTag;
    public string Fx { get => FemLoadsFormat.Kilo(Load.Fx); set => Set(value, 0); }
    public string Fy { get => FemLoadsFormat.Kilo(Load.Fy); set => Set(value, 1); }
    public string Fz { get => FemLoadsFormat.Kilo(Load.Fz); set => Set(value, 2); }
    public string Mx { get => FemLoadsFormat.Kilo(Load.Mx); set => Set(value, 3); }
    public string My { get => FemLoadsFormat.Kilo(Load.My); set => Set(value, 4); }
    public string Mz { get => FemLoadsFormat.Kilo(Load.Mz); set => Set(value, 5); }

    void Set(string text, int component)
    {
        if (!owner.TryNumber(text, this, out var kilo)) return;
        double[] v = [Load.Fx, Load.Fy, Load.Fz, Load.Mx, Load.My, Load.Mz];
        v[component] = kilo * 1e3;
        owner.Execute(new SetNodeLoadCommand(Load.LoadCaseId, Load.NodeId, v[0], v[1], v[2], v[3], v[4], v[5]));
    }
}

/// <summary>Нагрузка на конструктивный стержень: отступы, м; интенсивности, кН/м; моменты (сосредоточенная), кН·м.</summary>
public sealed class FemMemberLoadRow(FemLoadsTabVM owner, FemMemberLoad load, string memberTag) : FemLoadsRow
{
    public FemMemberLoad Load { get; } = load;
    public override object Source => Load;
    public string MemberTag { get; } = memberTag;
    bool IsPoint => Load.DistributionType == "point";
    bool IsUniform => Load.DistributionType == "uniform";

    public string CoordinateSystem { get => Load.CoordinateSystem; set => Edit(v => v.CoordinateSystem = value); }
    public string DistributionType
    {
        get => Load.DistributionType;
        set => Edit(v =>
        {
            v.DistributionType = value;
            if (value == "uniform") { v.QxEnd = v.QxStart; v.QyEnd = v.QyStart; v.QzEnd = v.QzStart; }
            if (value == "point") { v.QxEnd = v.QyEnd = v.QzEnd = 0; v.EndOffsetM = 0; }
            if (value != "point") v.Mx = v.My = v.Mz = 0;
        });
    }
    public string StartOffset { get => FemLoadsFormat.Number(Load.StartOffsetM); set => EditNumber(value, 1, (v, x) => v.StartOffsetM = x); }
    public string EndOffset { get => IsPoint ? "" : FemLoadsFormat.Number(Load.EndOffsetM); set => EditNumber(value, 1, (v, x) => v.EndOffsetM = x); }
    public string QxStart { get => FemLoadsFormat.Kilo(Load.QxStart); set => EditStart(value, (v, x) => v.QxStart = x, (v, x) => v.QxEnd = x); }
    public string QyStart { get => FemLoadsFormat.Kilo(Load.QyStart); set => EditStart(value, (v, x) => v.QyStart = x, (v, x) => v.QyEnd = x); }
    public string QzStart { get => FemLoadsFormat.Kilo(Load.QzStart); set => EditStart(value, (v, x) => v.QzStart = x, (v, x) => v.QzEnd = x); }
    public string QxEnd { get => IsPoint ? "" : FemLoadsFormat.Kilo(Load.QxEnd); set => EditEnd(value, (v, x) => v.QxEnd = x); }
    public string QyEnd { get => IsPoint ? "" : FemLoadsFormat.Kilo(Load.QyEnd); set => EditEnd(value, (v, x) => v.QyEnd = x); }
    public string QzEnd { get => IsPoint ? "" : FemLoadsFormat.Kilo(Load.QzEnd); set => EditEnd(value, (v, x) => v.QzEnd = x); }
    public string Mx { get => IsPoint ? FemLoadsFormat.Kilo(Load.Mx) : ""; set => EditMoment(value, (v, x) => v.Mx = x); }
    public string My { get => IsPoint ? FemLoadsFormat.Kilo(Load.My) : ""; set => EditMoment(value, (v, x) => v.My = x); }
    public string Mz { get => IsPoint ? FemLoadsFormat.Kilo(Load.Mz) : ""; set => EditMoment(value, (v, x) => v.Mz = x); }

    /// <summary>У равномерной конец равен началу; у сосредоточенной конца нет.</summary>
    void EditStart(string text, Action<FemMemberLoad, double> start, Action<FemMemberLoad, double> end) =>
        EditNumber(text, 1e3, (v, x) => { start(v, x); if (IsUniform) end(v, x); });

    void EditEnd(string text, Action<FemMemberLoad, double> end)
    {
        if (IsPoint) { Refresh(); return; }
        // Правка конца превращает равномерную нагрузку в трапециевидную.
        EditNumber(text, 1e3, (v, x) => { end(v, x); if (IsUniform) v.DistributionType = "trapezoidal"; });
    }

    void EditMoment(string text, Action<FemMemberLoad, double> apply)
    {
        if (!IsPoint) { Refresh(); return; }
        EditNumber(text, 1e3, apply);
    }

    void EditNumber(string text, double scale, Action<FemMemberLoad, double> apply)
    {
        if (!owner.TryNumber(text, this, out var x)) return;
        Edit(v => apply(v, x * scale));
    }

    void Edit(Action<FemMemberLoad> apply)
    {
        var values = SetMemberLoadCommand.Copy(Load);
        apply(values);
        owner.Execute(new EditMemberLoadCommand(Load, values));
    }
}

/// <summary>
/// Нагрузка на КЭ сетки (площадная и импортные виды): у ручной равномерной правятся значение (кПа) и направление —
/// заменой нагрузки; импортные — только чтение и удаление.
/// </summary>
public sealed class FemElementLoadRow(FemLoadsTabVM owner, FemElementLoad load, IReadOnlyList<FemMemberGroup> groups) : FemLoadsRow
{
    readonly FemElementLoadView _view = new(load, groups);
    public FemElementLoad Load { get; } = load;
    public override object Source => Load;
    public override bool IsEditable => _view.IsManual && Load.LoadKind == FemElementLoadKinds.Uniform;
    public string Kind => Loc.S("FemElementLoadKind_" + Load.LoadKind);
    public string Target => _view.TargetText;
    public string? OriginText => _view.IsManual ? null : FemMeshNodeRow.DescribeOrigin(Load.Origin);

    public string Value
    {
        get => _view.ValueText;
        set
        {
            if (!IsEditable) { Refresh(); return; }
            if (!owner.TryNumber(value, this, out var kpa)) return;
            if (kpa == 0) { owner.Reject(Loc.S("FemAreaLoadZero"), this); return; }
            Replace(l => l.SetValues([kpa * 1e3]));
        }
    }

    /// <summary>«global:z», «local:z» (по нормали)…</summary>
    public string Direction
    {
        get => Load.LoadKind == FemElementLoadKinds.SelfWeight ? "" : $"{(Load.IsLocal ? "local" : "global")}:{Load.Axis}";
        set
        {
            if (!IsEditable || value?.Split(':') is not [var system, var axis]) { Refresh(); return; }
            Replace(l => { l.CoordinateSystem = system; l.Axis = axis; });
        }
    }

    void Replace(Action<FemElementLoad> apply)
    {
        var next = FemLoadsTabVM.CopyElementLoad(Load);
        apply(next);
        owner.Execute(new ReplaceElementLoadCommand(Load, next));
    }
}

/// <summary>Заданное перемещение (мм) или поворот (мрад) узла КонЭ.</summary>
public sealed class FemKinematicLoadRow(FemLoadsTabVM owner, FemKinematicLoad load, string nodeTag) : FemLoadsRow
{
    public FemKinematicLoad Load { get; } = load;
    public override object Source => Load;
    public string NodeTag { get; } = nodeTag;

    /// <summary>Степень свободы 1…6 (Ux…Rz); смена — удалить и задать по новой одной отменой.</summary>
    public int Dof
    {
        get => Load.Dof;
        set
        {
            if (value is < 1 or > 6 || value == Load.Dof) return;
            owner.Execute(new CompositeFemCommand([
                new DeleteKinematicLoadCommand(Load),
                new SetKinematicLoadCommand(Load.LoadCaseId, Load.NodeId, value, Load.Value),
            ]));
        }
    }

    public string Value
    {
        get => FemLoadsFormat.Number(Load.Value * 1e3);
        set
        {
            if (!owner.TryNumber(value, this, out var milli)) return;
            owner.Execute(new SetKinematicLoadCommand(Load.LoadCaseId, Load.NodeId, Load.Dof, milli / 1e3));
        }
    }
}

/// <summary>Нагрузка на узел сетки (импорт): только чтение и удаление.</summary>
public sealed class FemMeshNodeLoadRow(FemMeshNodeLoad load) : FemLoadsRow
{
    public FemMeshNodeLoad Load { get; } = load;
    public override object Source => Load;
    public override bool IsEditable => false;
    public string NodeTag => Load.MeshNodeTag;
    public string Fx => FemLoadsFormat.Kilo(Load.Fx);
    public string Fy => FemLoadsFormat.Kilo(Load.Fy);
    public string Fz => FemLoadsFormat.Kilo(Load.Fz);
    public string Mx => FemLoadsFormat.Kilo(Load.Mx);
    public string My => FemLoadsFormat.Kilo(Load.My);
    public string Mz => FemLoadsFormat.Kilo(Load.Mz);
    public string? OriginText => Load.Origin == FemLoadOrigin.Manual ? null : FemMeshNodeRow.DescribeOrigin(Load.Origin);
}

/// <summary>Числа ячеек вкладки «Загружения».</summary>
public static class FemLoadsFormat
{
    public static string Number(double value) => value.ToString("0.######", CultureInfo.CurrentCulture);
    /// <summary>Н → кН, Н·м → кН·м, Па → кПа.</summary>
    public static string Kilo(double value) => Number(value / 1e3);
    public static string Optional(double? value) => value is { } v ? Number(v) : "";

    /// <summary>Пусто — null; иначе число (неотрицательное, если не <paramref name="allowNegative"/>).</summary>
    public static bool TryOptional(string? text, out double? value, bool allowNegative)
    {
        value = null;
        if (string.IsNullOrWhiteSpace(text)) return true;
        if (!Pars.ParseAny(text.Trim(), out var v) || !double.IsFinite(v) || (!allowNegative && v < 0)) return false;
        value = v;
        return true;
    }
}

/// <summary>
/// Вкладка «Загружения» редактора схемы: слева — загружения с правкой в ячейках (строка = активное загружение),
/// справа — нагрузки активного загружения по видам. Все правки — командами редактора (отмена, сохранение схемы
/// пользователем); таблицы перечитываются после каждой команды, при неизменном составе — без пересборки строк.
/// </summary>
public sealed class FemLoadsTabVM : ViewModelBase
{
    public FemSchemaEditorVM Editor { get; }

    public FemLoadsTabVM(FemSchemaEditorVM editor)
    {
        Editor = editor;
        editor.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName is nameof(FemSchemaEditorVM.Session)) Reload();
            else if (e.PropertyName is nameof(FemSchemaEditorVM.SelectedLoadCase)) { OnPropertyChanged(nameof(SelectedLoadCaseRow)); Reload(); }
        };
        Reload();
    }

    /// <summary>Вид величин нагрузок из настроек расчёта (при импортированных загружениях — предупреждение).</summary>
    public string ValueKindNote => Editor.Sp20ValueKindNote;
    public bool HasValueKindConflict => Editor.HasValueKindConflict;

    public static IReadOnlyList<FemLoadsChoice> Sp20Types { get; } =
    [
        new("permanent", Loc.S("FemLoadCaseTypePermanent")),
        new("long_term", Loc.S("FemLoadCaseTypeLongTerm")),
        new("short_term", Loc.S("FemLoadCaseTypeShortTerm")),
        new("accidental", Loc.S("FemLoadCaseTypeAccidental")),
    ];
    public static IReadOnlyList<FemLoadsChoice> CoordinateSystems { get; } =
        [new("local", Loc.S("FemMemberLoadLocal")), new("global", Loc.S("FemMemberLoadGlobal"))];
    public static IReadOnlyList<FemLoadsChoice> Distributions { get; } =
    [
        new("uniform", Loc.S("FemMemberLoadUniform")),
        new("trapezoidal", Loc.S("FemMemberLoadTrapezoidal")),
        new("point", Loc.S("FemMemberLoadPoint")),
    ];
    public static IReadOnlyList<FemLoadsChoice> AreaDirections { get; } =
        [new("global:x", "X"), new("global:y", "Y"), new("global:z", "Z"), new("local:z", Loc.S("FemAreaLoadDirNormal"))];
    public static IReadOnlyList<KeyValuePair<int, string>> Dofs { get; } =
        [new(1, "Ux"), new(2, "Uy"), new(3, "Uz"), new(4, "Rx"), new(5, "Ry"), new(6, "Rz")];

    IReadOnlyList<FemLoadCaseRow> _loadCaseRows = [];
    public IReadOnlyList<FemLoadCaseRow> LoadCaseRows { get => _loadCaseRows; private set { _loadCaseRows = value; OnPropertyChanged(); } }
    IReadOnlyList<FemNodeLoadRow> _nodeLoadRows = [];
    public IReadOnlyList<FemNodeLoadRow> NodeLoadRows { get => _nodeLoadRows; private set { _nodeLoadRows = value; OnPropertyChanged(); } }
    IReadOnlyList<FemMemberLoadRow> _memberLoadRows = [];
    public IReadOnlyList<FemMemberLoadRow> MemberLoadRows { get => _memberLoadRows; private set { _memberLoadRows = value; OnPropertyChanged(); } }
    IReadOnlyList<FemElementLoadRow> _elementLoadRows = [];
    public IReadOnlyList<FemElementLoadRow> ElementLoadRows { get => _elementLoadRows; private set { _elementLoadRows = value; OnPropertyChanged(); } }
    IReadOnlyList<FemKinematicLoadRow> _kinematicLoadRows = [];
    public IReadOnlyList<FemKinematicLoadRow> KinematicLoadRows { get => _kinematicLoadRows; private set { _kinematicLoadRows = value; OnPropertyChanged(); } }
    IReadOnlyList<FemMeshNodeLoadRow> _meshNodeLoadRows = [];
    public IReadOnlyList<FemMeshNodeLoadRow> MeshNodeLoadRows { get => _meshNodeLoadRows; private set { _meshNodeLoadRows = value; OnPropertyChanged(); } }

    /// <summary>Строка таблицы = активное загружение.</summary>
    public FemLoadCaseRow? SelectedLoadCaseRow
    {
        get => LoadCaseRows.FirstOrDefault(r => ReferenceEquals(r.LoadCase, Editor.SelectedLoadCase));
        set { if (value != null && !ReferenceEquals(Editor.SelectedLoadCase, value.LoadCase)) Editor.SelectedLoadCase = value.LoadCase; }
    }

    string? _status;
    /// <summary>Причина отклонённой правки или итог команды; пусто — нечего сообщать.</summary>
    public string? Status { get => _status; private set { _status = value; OnPropertyChanged(); } }

    /// <summary>Подписи вкладок с числом нагрузок активного загружения.</summary>
    public string NodeLoadsHeader => Header("FemLoadsTabNodal", NodeLoadRows.Count);
    public string MemberLoadsHeader => Header("FemLoadsTabMember", MemberLoadRows.Count);
    public string ElementLoadsHeader => Header("FemLoadsTabArea", ElementLoadRows.Count);
    public string KinematicLoadsHeader => Header("FemLoadsTabKinematic", KinematicLoadRows.Count);
    public string MeshNodeLoadsHeader => Header("FemLoadsTabMeshNodal", MeshNodeLoadRows.Count);
    static string Header(string key, int count) => count > 0 ? $"{Loc.S(key)} ({count})" : Loc.S(key);

    /// <summary>Перечитывает таблицы из сессии: состав не изменился — строки только обновляют значения.</summary>
    public void Reload()
    {
        OnPropertyChanged(nameof(ValueKindNote));
        OnPropertyChanged(nameof(HasValueKindConflict));
        var session = Editor.Session;
        var active = Editor.SelectedLoadCase;
        LoadCaseRows = Merge(LoadCaseRows, session.LoadCases, (lc, i) => new FemLoadCaseRow(this, lc, i + 1));

        var nodeTags = session.Nodes.GroupBy(n => n.Id).ToDictionary(g => g.Key, g => g.First().NodeTag);
        var memberTags = session.Members.GroupBy(m => m.Id).ToDictionary(g => g.Key, g => g.First().ElemTag);
        string NodeTag(int id) => nodeTags.TryGetValue(id, out var t) ? t : $"#{id}";
        int caseId = active?.Id ?? int.MinValue;
        bool InCase(int id) => active != null && id == caseId;

        NodeLoadRows = Merge(NodeLoadRows, session.NodeLoads.Where(l => InCase(l.LoadCaseId)).ToList(),
            (l, _) => new FemNodeLoadRow(this, l, NodeTag(l.NodeId)));
        MemberLoadRows = Merge(MemberLoadRows, session.MemberLoads.Where(l => InCase(l.LoadCaseId)).ToList(),
            (l, _) => new FemMemberLoadRow(this, l, memberTags.TryGetValue(l.MemberId, out var t) ? t : $"#{l.MemberId}"));
        ElementLoadRows = Merge(ElementLoadRows, session.ElementLoads.Where(l => InCase(l.LoadCaseId)).ToList(),
            (l, _) => new FemElementLoadRow(this, l, session.MemberGroups));
        KinematicLoadRows = Merge(KinematicLoadRows, session.KinematicLoads.Where(l => InCase(l.LoadCaseId)).ToList(),
            (l, _) => new FemKinematicLoadRow(this, l, NodeTag(l.NodeId)));
        MeshNodeLoadRows = Merge(MeshNodeLoadRows, session.MeshNodeLoads.Where(l => InCase(l.LoadCaseId)).ToList(),
            (l, _) => new FemMeshNodeLoadRow(l));
        foreach (var name in new[] { nameof(NodeLoadsHeader), nameof(MemberLoadsHeader), nameof(ElementLoadsHeader),
                     nameof(KinematicLoadsHeader), nameof(MeshNodeLoadsHeader), nameof(SelectedLoadCaseRow) })
            OnPropertyChanged(name);
    }

    internal static IReadOnlyList<TRow> Merge<TRow, TSource>(IReadOnlyList<TRow> rows, IReadOnlyList<TSource> sources, Func<TSource, int, TRow> create)
        where TRow : FemLoadsRow where TSource : class
    {
        if (rows.Count == sources.Count && rows.Select(r => r.Source).SequenceEqual(sources, ReferenceEqualityComparer.Instance))
        {
            foreach (var row in rows) row.Refresh();
            return rows;
        }
        return sources.Select(create).ToList();
    }

    // ── Правка ──────────────────────────────────────────────────────────────

    internal void Execute(IFemEditCommand command)
    {
        Status = null;
        Editor.Session.Execute(command);
        Editor.RefreshCollections();
    }

    internal void Reject(string reason, FemLoadsRow row)
    {
        Status = reason;
        row.Refresh();
    }

    internal bool TryNumber(string? text, FemLoadsRow row, out double value)
    {
        value = 0;
        if (!row.IsEditable) { row.Refresh(); return false; }
        if (text != null && Pars.ParseAny(text.Trim(), out value) && double.IsFinite(value)) return true;
        Reject(string.Format(Loc.S("FemLoadsBadNumber"), text), row);
        return false;
    }

    internal void EditLoadCase(FemLoadCaseRow row, Action<FemLoadCase> apply)
    {
        var lc = row.LoadCase;
        var values = new FemLoadCase
        {
            Tag = lc.Tag, LoadType = lc.LoadType, Sp20Type = lc.Sp20Type, Sp20Group = lc.Sp20Group,
            GammaFUnfav = lc.GammaFUnfav, GammaFFav = lc.GammaFFav, Psi1 = lc.Psi1, Psi2 = lc.Psi2,
            SelfWeightFactor = lc.SelfWeightFactor, LongTermShare = lc.LongTermShare,
        };
        apply(values);
        if (values.Tag == lc.Tag && values.Sp20Type == lc.Sp20Type && values.Sp20Group == lc.Sp20Group
            && values.GammaFUnfav == lc.GammaFUnfav && values.GammaFFav == lc.GammaFFav && values.Psi1 == lc.Psi1
            && values.Psi2 == lc.Psi2 && values.SelfWeightFactor == lc.SelfWeightFactor
            && values.LongTermShare == lc.LongTermShare) return;
        string oldTag = lc.Tag;
        Status = Editor.EditLoadCase(lc, values);
        if (Status != null) { row.Refresh(); return; }
        if (lc.Tag != oldTag && ReferenceEquals(Editor.SelectedLoadCase, lc)) LoadCaseRenamed?.Invoke(lc);
    }

    /// <summary>Вид нагрузки СП 20 нескольким загружениям одной отменой (контекстное меню таблицы).</summary>
    public void SetSp20Type(IEnumerable<FemLoadCaseRow> rows, string sp20Type)
    {
        Status = null;
        var commands = rows.Select(r => r.LoadCase).Distinct().Where(lc => lc.Sp20Type != sp20Type)
            .Select(lc => (IFemEditCommand)new EditLoadCaseCommand(lc, new FemLoadCase
            {
                Tag = lc.Tag, LoadType = lc.LoadType, Sp20Type = sp20Type, Sp20Group = lc.Sp20Group,
                GammaFUnfav = lc.GammaFUnfav, GammaFFav = lc.GammaFFav, Psi1 = lc.Psi1, Psi2 = lc.Psi2,
                SelfWeightFactor = lc.SelfWeightFactor, LongTermShare = lc.LongTermShare,
            })).ToList();
        if (commands.Count > 0) Execute(Editor.WithAutoRecompute(Group(commands)));
    }

    /// <summary>Активное загружение переименовано в таблице — запомнить новое имя (активное хранится по имени).</summary>
    public event Action<FemLoadCase>? LoadCaseRenamed;

    // ── Команды панели ──────────────────────────────────────────────────────

    /// <summary>Новое загружение (кратковременное) становится активным.</summary>
    public void AddLoadCase()
    {
        Status = null;
        var lc = Editor.AddLoadCase(Loc.S("FemLoadCaseDefaultTag"), "short_term");
        Editor.SelectedLoadCase = lc;
    }

    /// <summary>Удаляет активное загружение с его нагрузками и слагаемыми комбинаций — одной отменой.</summary>
    public void DeleteLoadCase()
    {
        Status = null;
        if (Editor.SelectedLoadCase is { } lc) Editor.DeleteLoadCase(lc);
        Editor.SelectedLoadCase ??= Editor.Session.LoadCases.FirstOrDefault();
    }

    /// <summary>Нулевые узловые нагрузки выбранным в 3D узлам КонЭ (у кого в загружении её нет) — значения в ячейках.</summary>
    public void AddNodeLoadsToSelection()
    {
        if (Editor.SelectedLoadCase is not { } lc) { Status = Loc.S("FemAreaLoadNoCase"); return; }
        var nodes = SelectedNodes().Where(n => !Editor.Session.NodeLoads.Any(l => l.LoadCaseId == lc.Id && l.NodeId == n.Id)).ToList();
        if (nodes.Count == 0) { Status = Loc.S("FemLoadsSelectNodes"); return; }
        Execute(Group(nodes.Select(n => (IFemEditCommand)new SetNodeLoadCommand(lc.Id, n.Id, 0, 0, 0, 0, 0, 0))));
    }

    /// <summary>Равномерная нагрузка (местные оси, нулевая) выбранным в 3D стержням КонЭ — значения в ячейках.</summary>
    public void AddMemberLoadsToSelection()
    {
        if (Editor.SelectedLoadCase is not { } lc) { Status = Loc.S("FemAreaLoadNoCase"); return; }
        var tags = Editor.Selection.SelectedElemTags.ToHashSet(StringComparer.Ordinal);
        var bars = Editor.Session.Members.Where(m => m.ElemType == "beam" && tags.Contains(m.ElemTag)).ToList();
        if (bars.Count == 0) { Status = Loc.S("FemLoadsSelectBars"); return; }
        Execute(Group(bars.Select(m => (IFemEditCommand)new SetMemberLoadCommand(new FemMemberLoad
        {
            SchemaId = Editor.Session.Schema.Id, LoadCaseId = lc.Id, MemberId = m.Id,
            CoordinateSystem = "local", DistributionType = "uniform",
        }))));
    }

    /// <summary>Нулевое заданное перемещение Uz выбранным в 3D узлам КонЭ — DOF и значение в ячейках.</summary>
    public void AddKinematicLoadsToSelection()
    {
        if (Editor.SelectedLoadCase is not { } lc) { Status = Loc.S("FemAreaLoadNoCase"); return; }
        var nodes = SelectedNodes().Where(n => !Editor.Session.KinematicLoads.Any(l => l.LoadCaseId == lc.Id && l.NodeId == n.Id && l.Dof == 3)).ToList();
        if (nodes.Count == 0) { Status = Loc.S("FemLoadsSelectNodes"); return; }
        Execute(Group(nodes.Select(n => (IFemEditCommand)new SetKinematicLoadCommand(lc.Id, n.Id, 3, 0))));
    }

    /// <summary>Площадная нагрузка из диалога (цель — выделенное, группа или список КЭ).</summary>
    public void AddAreaLoad(string targetMode, FemMemberGroup? group, string? tagsText, double pressurePa, string direction)
    {
        var dir = direction.Split(':');
        Status = Editor.ApplyAreaLoad(targetMode, group, tagsText, pressurePa, dir[0], dir[1]);
    }

    /// <summary>Удаляет строки (любых видов нагрузок) одной отменой.</summary>
    public void DeleteRows(IEnumerable<FemLoadsRow> rows)
    {
        var commands = rows.Select(r => r.Source switch
        {
            FemNodeLoad l => new DeleteNodeLoadCommand(l),
            FemMemberLoad l => new DeleteMemberLoadCommand(l),
            FemElementLoad l => new DeleteElementLoadCommand(l),
            FemKinematicLoad l => new DeleteKinematicLoadCommand(l),
            FemMeshNodeLoad l => (IFemEditCommand)new DeleteMeshNodeLoadCommand(l),
            _ => null,
        }).OfType<IFemEditCommand>().ToList();
        if (commands.Count > 0) Execute(Group(commands));
    }

    IEnumerable<FemNode> SelectedNodes()
    {
        var tags = Editor.Selection.SelectedNodeTags.ToHashSet(StringComparer.Ordinal);
        return Editor.Session.Nodes.Where(n => tags.Contains(n.NodeTag));
    }

    static IFemEditCommand Group(IEnumerable<IFemEditCommand> commands)
    {
        var list = commands.ToList();
        return list.Count == 1 ? list[0] : new CompositeFemCommand(list);
    }

    /// <summary>Копия нагрузки на КЭ для замены (<see cref="ReplaceElementLoadCommand"/>).</summary>
    public static FemElementLoad CopyElementLoad(FemElementLoad source)
    {
        var copy = new FemElementLoad
        {
            Id = source.Id, SchemaId = source.SchemaId, LoadCaseId = source.LoadCaseId, Origin = source.Origin,
            TargetKind = source.TargetKind, GroupId = source.GroupId, LoadKind = source.LoadKind,
            CoordinateSystem = source.CoordinateSystem, Axis = source.Axis,
        };
        copy.SetTargetTags(source.TargetTags);
        copy.SetValues(source.Values);
        return copy;
    }
}
