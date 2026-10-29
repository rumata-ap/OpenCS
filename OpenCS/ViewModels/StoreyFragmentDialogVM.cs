using System.Globalization;
using CScore.Fem.Fragment;
using OpenCS.Services;
using OpenCS.Utilites;

namespace OpenCS.ViewModels;

/// <summary>Строка списка перекрытий диалога фрагмента: индекс уровня и подпись «+3,300 · 640 м² · КонЭ: 8».</summary>
public sealed record StoreyFloorRow(int Index, StoreyFloorLevel Level, string Label)
{
    public override string ToString() => Label;
}

/// <summary>
/// Диалог «Фрагмент этажа…» (спека 2026-10-29): перекрытие из списка (предвыбор — из 3D), отметки z₁/z₂ — середины этажей
/// ниже и выше, без соседа — край схемы; «Задать отметки вручную» открывает поля z₁/z₂. Имя следует за выбором, пока
/// пользователь не ввёл своё.
/// </summary>
public sealed class StoreyFragmentDialogVM : ViewModelBase
{
    readonly string _parentTag;
    readonly StoreyFloorContext _context;
    readonly CultureInfo _culture;
    StoreyFloorRow? _selected;
    bool _manual, _nameEdited, _settingName;
    string _z1Text = "", _z2Text = "", _name = "", _belowText = "", _aboveText = "", _warning = "";

    /// <param name="pickedZ">Отметка перекрытия по выделению в 3D; null — без предвыбора.</param>
    /// <param name="pickErrorKey">Ключ строки отказа отметки по выделению (показывается в диалоге).</param>
    public StoreyFragmentDialogVM(string parentTag, StoreyFloorContext context, double? pickedZ, string? pickErrorKey,
        CultureInfo culture)
    {
        _parentTag = parentTag;
        _context = context;
        _culture = culture;
        Levels = context.Levels
            .Select((l, i) => new StoreyFloorRow(i, l,
                string.Format(culture, Loc.S("StoreyFloorRow"), Signed(l.Z), l.AreaM2.ToString("0.#", culture), Counts(l))))
            .ToList();
        RangeText = string.Format(Loc.S("StoreyFragmentMeshRange"), F(context.ZMin), F(context.ZMax));
        if (pickErrorKey is not null) PickMessage = Loc.S(pickErrorKey);
        if (Levels.Count == 0)
        {
            PickMessage = Loc.S("StoreyFloorNone");
            Manual = true;
            return;
        }
        if (pickedZ is { } z)
        {
            if (StoreyFloorLevels.LevelAt(context.Levels, z) is int index) SelectedLevel = Levels[index];
            else PickMessage = string.Format(Loc.S("StoreyFloorPickNotFound"), Signed(z));
        }
    }

    public string ParentTag => _parentTag;
    public IReadOnlyList<StoreyFloorRow> Levels { get; }
    public bool HasLevels => Levels.Count > 0;
    /// <summary>Диапазон высот схемы (подсказка).</summary>
    public string RangeText { get; }
    /// <summary>Сообщение о предвыборе из 3D (отказ, не найдено) или об отсутствии перекрытий; пусто — нет.</summary>
    public string PickMessage { get; } = "";

    public StoreyFloorRow? SelectedLevel
    {
        get => _selected;
        set { _selected = value; OnPropertyChanged(); Recompute(); }
    }

    /// <summary>Ручной режим: поля z₁/z₂ редактируются; при выключении отметки пересчитываются по перекрытию.</summary>
    public bool Manual
    {
        get => _manual;
        set
        {
            if (!value && !HasLevels) value = true;
            _manual = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(Auto));
            Recompute();
        }
    }

    public bool Auto => !_manual;

    public string Z1Text { get => _z1Text; set { _z1Text = value; OnPropertyChanged(); ManualNameFollow(); } }
    public string Z2Text { get => _z2Text; set { _z2Text = value; OnPropertyChanged(); ManualNameFollow(); } }
    public string BelowText { get => _belowText; private set { _belowText = value; OnPropertyChanged(); } }
    public string AboveText { get => _aboveText; private set { _aboveText = value; OnPropertyChanged(); } }
    /// <summary>Предупреждение о разрезе по другому перекрытию; пусто — нет.</summary>
    public string Warning { get => _warning; private set { _warning = value; OnPropertyChanged(); } }

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

    /// <summary>Запрос сборки; ошибка — текст для пользователя, запрос null.</summary>
    public StoreyFragmentRequest? TryRequest(out string? error)
    {
        if (!_manual && _selected is null)
        {
            error = Loc.S("StoreyFloorNotSelected");
            return null;
        }
        return StoreyFragmentForm.TryRead(Z1Text, Z2Text, Name, out error);
    }

    void Recompute()
    {
        if (_manual)
        {
            BelowText = AboveText = Warning = "";
            ManualNameFollow();
            return;
        }
        if (_selected is null)
        {
            BelowText = AboveText = Warning = "";
            _z1Text = _z2Text = "";
            OnPropertyChanged(nameof(Z1Text));
            OnPropertyChanged(nameof(Z2Text));
            return;
        }
        var band = StoreyFloorLevels.Band(_context.Levels, _selected.Index, _context.ZMin, _context.ZMax);
        _z1Text = Exact(band.Z1);
        _z2Text = Exact(band.Z2);
        OnPropertyChanged(nameof(Z1Text));
        OnPropertyChanged(nameof(Z2Text));
        BelowText = band.HeightBelow is { } hb
            ? string.Format(Loc.S("StoreyFloorBelow"), F(hb), Signed(band.Z1))
            : string.Format(Loc.S("StoreyFloorBelowEdge"), Signed(band.Z1));
        AboveText = band.HeightAbove is { } ha
            ? string.Format(Loc.S("StoreyFloorAbove"), F(ha), Signed(band.Z2))
            : string.Format(Loc.S("StoreyFloorAboveEdge"), Signed(band.Z2));
        Warning = band.CrossedLevelZ.Count == 0 ? ""
            : string.Format(Loc.S("StoreyFloorCrossed"), string.Join("; ", band.CrossedLevelZ.Select(Signed)));
        SetDefaultName(StoreyFragmentForm.DefaultName(_parentTag, _selected.Level.Z, _culture));
    }

    void ManualNameFollow()
    {
        if (!_manual) return;
        if (Pars.ParseAny(_z1Text.Trim(), out var z1) && Pars.ParseAny(_z2Text.Trim(), out var z2))
            SetDefaultName(StoreyFragmentForm.DefaultName(_parentTag, z1, z2, _culture));
    }

    void SetDefaultName(string name)
    {
        if (_nameEdited) return;
        _settingName = true;
        Name = name;
        _settingName = false;
    }

    /// <summary>«КонЭ: 8», «КЭ: 15695» или оба через запятую.</summary>
    static string Counts(StoreyFloorLevel l) => string.Join(", ", new[]
    {
        l.MemberTags.Count > 0 ? string.Format(Loc.S("StoreyFloorMembers"), l.MemberTags.Count) : null,
        l.ElementCount > 0 ? string.Format(Loc.S("StoreyFloorElements"), l.ElementCount) : null,
    }.OfType<string>());

    string F(double v) => v.ToString("0.###", _culture);
    string Signed(double v) => StoreyFragmentForm.SignedLevel(v, _culture);
    string Exact(double v) => v.ToString("0.######", _culture);
}
