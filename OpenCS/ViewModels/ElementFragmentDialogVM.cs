using System.Globalization;
using CScore.Fem.Fragment;
using OpenCS.Services;
using OpenCS.Utilites;

namespace OpenCS.ViewModels;

/// <summary>
/// Диалог «Фрагмент вокруг элементов…» (спека 2026-10-30, разд. 5): выбранные колонны и стены, отступ d, перекрытия «с … по …»
/// (по умолчанию одно — ближайшее к середине элементов) или «вся высота элементов», имя, справка о призме и отметках разрезов.
/// Смена d пересчитывает призму и доступные перекрытия через <see cref="ApplyOffsetAsync"/> (последний запрос побеждает).
/// </summary>
public sealed class ElementFragmentDialogVM : ViewModelBase
{
    readonly Func<double, Task<ElementFragmentSelection>> _recompute;
    readonly CultureInfo _culture;
    ElementFragmentSelection _selection;
    IReadOnlyList<StoreyFloorRow> _levels = [];
    StoreyFloorRow? _from, _to;
    bool _fullHeight, _busy, _nameEdited, _settingName, _settingLevels;
    string _offsetText, _name = "", _message = "", _prismText = "", _rangeText = "", _offsetError = "";
    int _version;

    /// <summary>Отступ больше этого (м) — вероятная опечатка (обычно 1–3 м): предупреждение в <see cref="Message"/>, сборку не запрещает.</summary>
    public const double LargeOffsetM = 10;

    /// <param name="parentTag">Имя схемы-родителя.</param>
    /// <param name="selection">Выбор по d по умолчанию.</param>
    /// <param name="recompute">Пересчёт выбора при новом d (в фоне).</param>
    /// <param name="rejectedTags">Невертикальные КонЭ, отсечённые до диалога (для сообщения).</param>
    public ElementFragmentDialogVM(string parentTag, ElementFragmentSelection selection,
        Func<double, Task<ElementFragmentSelection>> recompute, IReadOnlyCollection<string> rejectedTags, CultureInfo culture)
    {
        ArgumentNullException.ThrowIfNull(selection);
        ParentTag = parentTag;
        _recompute = recompute;
        _culture = culture;
        _selection = selection;
        _offsetText = selection.OffsetM.ToString("0.###", culture);
        MemberTagsText = string.Join(", ", selection.MemberTags);
        RejectedText = rejectedTags.Count == 0 ? ""
            : string.Format(Loc.S("ElementFragmentRejected"), TagList(rejectedTags));
        Apply(selection, keepChoice: false);
    }

    public string ParentTag { get; }
    /// <summary>Выбранные КонЭ (только чтение).</summary>
    public string MemberTagsText { get; }
    /// <summary>Сообщение об отсечённых невертикальных КонЭ; пусто — нет.</summary>
    public string RejectedText { get; }

    /// <summary>Отступ d, м (текст поля).</summary>
    public string OffsetText
    {
        get => _offsetText;
        set { _offsetText = value; OnPropertyChanged(); }
    }

    /// <summary>Доступные крайние перекрытия по возрастанию отметки.</summary>
    public IReadOnlyList<StoreyFloorRow> Levels { get => _levels; private set { _levels = value; OnPropertyChanged(); OnPropertyChanged(nameof(HasLevels)); } }
    public bool HasLevels => _levels.Count > 0;

    /// <summary>Нижнее перекрытие i; выбор выше верхнего подтягивает верхнее.</summary>
    public StoreyFloorRow? FromLevel
    {
        get => _from;
        set
        {
            if (value == _from) return;
            _from = value;
            OnPropertyChanged();
            if (!_settingLevels && value is not null && _to is not null && _to.Level.Z < value.Level.Z) { _to = value; OnPropertyChanged(nameof(ToLevel)); }
            if (!_settingLevels) Recompute();
        }
    }

    /// <summary>Верхнее перекрытие k; выбор ниже нижнего подтягивает нижнее.</summary>
    public StoreyFloorRow? ToLevel
    {
        get => _to;
        set
        {
            if (value == _to) return;
            _to = value;
            OnPropertyChanged();
            if (!_settingLevels && value is not null && _from is not null && _from.Level.Z > value.Level.Z) { _from = value; OnPropertyChanged(nameof(FromLevel)); }
            if (!_settingLevels) Recompute();
        }
    }

    /// <summary>«Вся высота элементов»; без перекрытий — всегда включена.</summary>
    public bool FullHeight
    {
        get => _fullHeight;
        set
        {
            if (!value && !HasLevels) value = true;
            if (value == _fullHeight) { OnPropertyChanged(); return; }
            _fullHeight = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(CanPickLevels));
            Recompute();
        }
    }

    /// <summary>Выпадающие перекрытий доступны: перекрытия есть и не «вся высота».</summary>
    public bool CanPickLevels => HasLevels && !_fullHeight;
    /// <summary>«Вся высота» можно выключить — есть перекрытия.</summary>
    public bool CanToggleFullHeight => HasLevels;

    /// <summary>Справка: размеры призмы, ось и центр.</summary>
    public string PrismText { get => _prismText; private set { _prismText = value; OnPropertyChanged(); } }
    /// <summary>Справка: отметки разрезов z₁…z₂ и перекрытия.</summary>
    public string RangeText { get => _rangeText; private set { _rangeText = value; OnPropertyChanged(); } }
    /// <summary>Отказы и предупреждения выбора, ошибка d; пусто — нет.</summary>
    public string Message { get => _message; private set { _message = value; OnPropertyChanged(); } }

    /// <summary>Идёт пересчёт призмы.</summary>
    public bool IsBusy { get => _busy; private set { _busy = value; OnPropertyChanged(); OnPropertyChanged(nameof(CanBuild)); } }
    /// <summary>Можно собирать: призма построена, d принят, пересчёт не идёт.</summary>
    public bool CanBuild => !_busy && !_selection.HasErrors && _offsetError.Length == 0;

    public string Name
    {
        get => _name;
        set
        {
            _name = value;
            if (!_settingName) _nameEdited = value.Length > 0;
            OnPropertyChanged();
        }
    }

    /// <summary>
    /// Принять d из поля: не число или ≤ 0 — ошибка в <see cref="Message"/>; то же d — без пересчёта; иначе пересчёт выбора
    /// (выбранные перекрытия сохраняются по отметке, если остались доступны).
    /// </summary>
    public async Task ApplyOffsetAsync()
    {
        if (!Pars.ParseAny(_offsetText.Trim(), out double d) || !(d > 0))
        {
            _offsetError = Loc.S("ElementFragmentOffsetInvalid");
            UpdateMessage();
            OnPropertyChanged(nameof(CanBuild));
            return;
        }
        bool hadError = _offsetError.Length > 0;
        _offsetError = "";
        if (Math.Abs(d - _selection.OffsetM) < 1e-9)
        {
            if (hadError) { UpdateMessage(); OnPropertyChanged(nameof(CanBuild)); }
            return;
        }
        int version = ++_version;
        IsBusy = true;
        try
        {
            var selection = await _recompute(d);
            if (version != _version) return;
            Apply(selection, keepChoice: true);
        }
        finally
        {
            if (version == _version) IsBusy = false;
        }
    }

    /// <summary>«Сохранять сетку родителя у необрезанных элементов» (<see cref="StoreyFragmentRequest.KeepParentMesh"/>).</summary>
    public bool KeepParentMesh { get; set; } = true;

    /// <summary>Запрос сборки; ошибка — текст для пользователя, запрос null.</summary>
    public StoreyFragmentRequest? TryRequest(out string? error)
    {
        error = null;
        if (_busy) { error = Loc.S("ElementFragmentBusy"); return null; }
        if (_offsetError.Length > 0) { error = _offsetError; return null; }
        if (_selection.HasErrors) { error = Message.Length > 0 ? Message : Loc.S("ElementFragmentRefused"); return null; }
        string name = Name.Trim();
        if (name.Length == 0) { error = Loc.S("StoreyFragmentNameEmpty"); return null; }
        if (_fullHeight) return ElementFragmentService.FullHeightRequest(_selection, name) with { KeepParentMesh = KeepParentMesh };
        if (_from is null || _to is null) { error = Loc.S("StoreyFloorNotSelected"); return null; }
        return ElementFragmentService.Request(_selection, _from.Index, _to.Index, name) with { KeepParentMesh = KeepParentMesh };
    }

    void Apply(ElementFragmentSelection selection, bool keepChoice)
    {
        double? fromZ = keepChoice ? _from?.Level.Z : null, toZ = keepChoice ? _to?.Level.Z : null;
        _selection = selection;
        Levels = selection.Height.Available
            .Select(i => new StoreyFloorRow(i, selection.Levels[i], string.Format(_culture, Loc.S("StoreyFloorRow"),
                Signed(selection.Levels[i].Z), selection.Levels[i].AreaM2.ToString("0.#", _culture),
                StoreyFragmentDialogVM.Counts(selection.Levels[i]))))
            .ToList();
        StoreyFloorRow? Find(double? z) => z is { } v
            ? _levels.FirstOrDefault(r => Math.Abs(r.Level.Z - v) <= StoreyFloorLevels.LevelToleranceM) : null;
        var def = _levels.FirstOrDefault(r => r.Index == selection.Height.Default);
        _settingLevels = true;
        FromLevel = Find(fromZ) ?? def;
        ToLevel = Find(toZ) ?? def;
        if (_from is not null && _to is not null && _to.Level.Z < _from.Level.Z) ToLevel = _from;
        _settingLevels = false;
        if (!HasLevels) _fullHeight = true;
        OnPropertyChanged(nameof(FullHeight));
        OnPropertyChanged(nameof(CanPickLevels));
        OnPropertyChanged(nameof(CanToggleFullHeight));
        OnPropertyChanged(nameof(CanBuild));
        PrismText = selection.Prism is { } p
            ? string.Format(_culture, Loc.S("ElementFragmentPrism"), F(2 * p.HalfLength), F(2 * p.HalfWidth),
                (Math.Atan2(p.AxisY, p.AxisX) * 180 / Math.PI).ToString("0.#", _culture), F(p.CenterX), F(p.CenterY), F(selection.OffsetM))
            : "";
        UpdateMessage();
        Recompute();
    }

    void Recompute()
    {
        if (_selection.HasErrors || _selection.Prism is null)
        {
            RangeText = "";
            return;
        }
        string elements = string.Format(Loc.S("ElementFragmentElementsZ"), Signed(_selection.ElemZMin), Signed(_selection.ElemZMax));
        double z1, z2;
        string floors;
        if (_fullHeight && _selection.Height is not { FullFrom: int, FullTo: int })
        {
            (z1, z2) = ElementFragmentLevels.FullHeightNoFloors(_selection.ElemZMin, _selection.ElemZMax);
            floors = Loc.S("ElementFragmentNoFloors");
        }
        else
        {
            int? i = _fullHeight ? _selection.Height.FullFrom : _from?.Index;
            int? k = _fullHeight ? _selection.Height.FullTo : _to?.Index;
            if (i is not int a || k is not int b)
            {
                RangeText = elements;
                return;
            }
            (z1, z2) = ElementFragmentLevels.Range(_selection.Levels, a, b, _selection.SchemaZMin, _selection.SchemaZMax);
            floors = a == b
                ? string.Format(Loc.S("ElementFragmentFloorOne"), Signed(_selection.Levels[a].Z))
                : string.Format(Loc.S("ElementFragmentFloorRange"), Signed(_selection.Levels[a].Z), Signed(_selection.Levels[b].Z));
        }
        RangeText = string.Join(Environment.NewLine, elements, floors,
            string.Format(Loc.S("ElementFragmentCuts"), Signed(z1), Signed(z2)));
        SetDefaultName(DefaultName(_selection.MemberTags));
    }

    void UpdateMessage()
    {
        var parts = new List<string>();
        if (RejectedText.Length > 0) parts.Add(RejectedText);
        if (_offsetError.Length > 0) parts.Add(_offsetError);
        else if (_selection.OffsetM > LargeOffsetM)
            parts.Add(string.Format(Loc.S("ElementFragmentOffsetLarge"), _selection.OffsetM.ToString("0.###", _culture),
                LargeOffsetM.ToString("0.###", _culture)));
        parts.AddRange(_selection.Diagnostics.Select(d => d.Message));
        Message = string.Join(Environment.NewLine, parts);
    }

    /// <summary>«Фрагмент · K1» или «Фрагмент · K1 +2».</summary>
    public static string DefaultName(IReadOnlyList<string> tags) => tags.Count switch
    {
        0 => Loc.S("ElementFragmentNameEmpty"),
        1 => string.Format(Loc.S("ElementFragmentName"), tags[0]),
        _ => string.Format(Loc.S("ElementFragmentNameMore"), tags[0], tags.Count - 1),
    };

    /// <summary>До 10 тегов через запятую, дальше «и ещё N».</summary>
    public static string TagList(IReadOnlyCollection<string> tags)
    {
        const int max = 10;
        string head = string.Join(", ", tags.Take(max));
        return tags.Count <= max ? head : string.Format(Loc.S("ElementFragmentTagsMore"), head, tags.Count - max);
    }

    void SetDefaultName(string name)
    {
        if (_nameEdited) return;
        _settingName = true;
        Name = name;
        _settingName = false;
    }

    string F(double v) => v.ToString("0.###", _culture);
    string Signed(double v) => StoreyFragmentForm.SignedLevel(v, _culture);
}
