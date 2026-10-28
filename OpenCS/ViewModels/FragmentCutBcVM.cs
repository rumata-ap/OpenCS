using System.Collections.ObjectModel;
using System.Globalization;
using System.Windows.Input;
using CScore.Fem;
using CScore.Fem.Fragment;
using CScore.Fem.Fragment.Masks;
using CScore.Planar;
using OpenCS.Services;
using OpenCS.Utilites;

namespace OpenCS.ViewModels;

/// <summary>Пункт выбора пресета ГУ разрезов.</summary>
public sealed record FragmentBcPresetOption(FragmentBcPreset Preset, string Label);

/// <summary>Пункт выбора режима DOF разреза; <see cref="Mode"/> = null — «по пресету».</summary>
public sealed record FragmentDofModeOption(FragmentDofMode? Mode, string Label);

/// <summary>
/// Ячейка режима одного DOF разреза: первый пункт — «по пресету (действующий режим)», затем четыре режима. Выбор
/// режима — правка поверх пресета (<see cref="IsOverride"/>).
/// </summary>
public sealed class FragmentCutDofCell : ViewModelBase
{
    readonly Action _changed;

    internal FragmentCutDofCell(FragmentDofMode presetMode, FragmentDofMode? overrideMode, Action changed)
    {
        _changed = changed;
        Options = [new(null, string.Format(Loc.S("FragmentCutBcByPreset"), FragmentCutBcVM.ModeLabel(presetMode))),
            .. Enum.GetValues<FragmentDofMode>().Select(m => new FragmentDofModeOption(m, FragmentCutBcVM.ModeLabel(m)))];
        _selected = Options.First(o => o.Mode == overrideMode);
        PresetMode = presetMode;
    }

    public IReadOnlyList<FragmentDofModeOption> Options { get; }
    /// <summary>Режим по пресету (с учётом сверлильного поворота стены).</summary>
    public FragmentDofMode PresetMode { get; }
    /// <summary>Действующий режим: правка или пресет.</summary>
    public FragmentDofMode Effective => _selected.Mode ?? PresetMode;
    public bool IsOverride => _selected.Mode != null;

    FragmentDofModeOption _selected;
    public FragmentDofModeOption Selected
    {
        get => _selected;
        set
        {
            if (value == null || value == _selected) return;
            _selected = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(IsOverride));
            _changed();
        }
    }
}

/// <summary>Строка таблицы ГУ: разрез фрагмента и шесть ячеек режимов (общие оси <c>Ux Uy Uz Rx Ry Rz</c>).</summary>
public sealed class FragmentCutBcRow
{
    internal FragmentCutBcRow(int cutId, FragmentCut cut, bool hasMask, IReadOnlyList<FragmentCutDofCell> dofs)
    {
        CutId = cutId;
        Cut = cut;
        HasMask = hasMask;
        Dofs = dofs;
    }

    public int CutId { get; }
    public FragmentCut Cut { get; }
    public string MemberTag => Cut.MemberTag;
    public string SideText => Loc.S(Cut.Side == FragmentCutSide.Bottom ? "FragmentCutSideBottom" : "FragmentCutSideTop");
    public string ZText => Cut.Z.ToString("0.###", CultureInfo.CurrentCulture);
    public string KindText => Loc.S(Cut.Kind == FragmentCutKind.End ? "FragmentCutKindEnd" : "FragmentCutKindEdge");
    public bool HasMask { get; }
    public string MaskText => Loc.S(HasMask ? "FragmentCutMaskYes" : "FragmentCutMaskNo");
    public IReadOnlyList<FragmentCutDofCell> Dofs { get; }
    public FragmentCutDofCell Ux => Dofs[0];
    public FragmentCutDofCell Uy => Dofs[1];
    public FragmentCutDofCell Uz => Dofs[2];
    public FragmentCutDofCell Rx => Dofs[3];
    public FragmentCutDofCell Ry => Dofs[4];
    public FragmentCutDofCell Rz => Dofs[5];

    /// <summary>Правки строки → 6 байт БД (255 — «по пресету»).</summary>
    internal byte[] Encode() => FragmentCutBc.Encode(Dofs.Select(d => d.Selected.Mode).ToList());
}

/// <summary>
/// Вкладка «ГУ разрезов» фрагмента (спека масок 5.1): пресет схемы и таблица режимов DOF по разрезам поверх пресета.
/// Изменения пишутся в БД сразу (<see cref="DatabaseService.SetFragmentBcPreset"/>,
/// <see cref="DatabaseService.ReplaceFragmentCutBc"/>); смена пресета правки не трогает, «Сбросить правки» — все «по
/// пресету». Пресет пишется в экземпляр схемы из <see cref="DatabaseService.FemSchemas"/> — его читает сборка входа CSfea.
/// </summary>
public sealed class FragmentCutBcVM : ViewModelBase
{
    readonly DatabaseService _db;
    readonly FemSchema _schema;
    List<(int Id, FragmentCut Cut)> _cuts = [];
    List<PlanarVector3?> _normals = [];
    HashSet<int> _maskedCuts = [];
    bool _hasMasks, _oblique, _loading;

    public FragmentCutBcVM(DatabaseService db, FemSchema schema)
    {
        _db = db;
        _schema = schema;
        IsVisible = schema.ParentSchemaId is not null;
        Presets = Enum.GetValues<FragmentBcPreset>().Select(p => new FragmentBcPresetOption(p, PresetLabel(p))).ToList();
        _selectedPreset = Presets.First(p => p.Preset == FragmentCutBc.ParsePreset(schema.FragmentBcPreset));
        ResetCommand = new RelayCommand(_ => ResetOverrides(), _ => Rows.Any(r => r.Dofs.Any(d => d.IsOverride)));
        if (IsVisible) Reload();
    }

    public bool IsVisible { get; }
    public IReadOnlyList<FragmentBcPresetOption> Presets { get; }
    public ObservableCollection<FragmentCutBcRow> Rows { get; } = [];
    public ICommand ResetCommand { get; }

    FragmentBcPresetOption _selectedPreset;
    public FragmentBcPresetOption SelectedPreset
    {
        get => _selectedPreset;
        set
        {
            if (value == null || value == _selectedPreset) return;
            _selectedPreset = value;
            OnPropertyChanged();
            string? stored = value.Preset == FragmentBcPreset.Kinematic ? null : value.Preset.ToString();
            var target = _db.FemSchemas.FirstOrDefault(s => s.Id == _schema.Id) ?? _schema;
            _db.SetFragmentBcPreset(target, stored);
            _schema.FragmentBcPreset = stored;
            BuildRows();
        }
    }

    string? _hint;
    /// <summary>Подсказка над таблицей: режимы требуют маски, а их нет; косые стены.</summary>
    public string? Hint { get => _hint; private set { _hint = value; OnPropertyChanged(); OnPropertyChanged(nameof(HasHint)); } }
    public bool HasHint => !string.IsNullOrEmpty(Hint);

    /// <summary>Перечитать разрезы, правки и маски (после снятия или удаления масок).</summary>
    public void Reload()
    {
        _cuts = _db.GetFragmentCutsWithIds(_schema.Id);
        _normals = StoreyFragmentMaskService.CutInputs(_db, _schema.Id, _cuts.Select(c => c.Cut).ToList())
            .Select(c => c.WallNormal).ToList();
        var masks = _db.GetFragmentMasks(_schema.Id);
        _hasMasks = masks is { Items.Count: > 0 };
        _maskedCuts = masks?.Items.Select(i => i.CutId).ToHashSet() ?? [];
        BuildRows();
    }

    void BuildRows()
    {
        var preset = _selectedPreset.Preset;
        var overrides = _db.GetFragmentCutBc(_schema.Id);
        _oblique = false;
        _loading = true;
        Rows.Clear();
        for (int i = 0; i < _cuts.Count; i++)
        {
            var (id, cut) = _cuts[i];
            var presetModes = FragmentCutBc.Resolve(preset, cut, null, _normals[i], out bool o);
            _oblique |= o;
            var stored = overrides.GetValueOrDefault(id);
            var cells = Enumerable.Range(0, 6).Select(d => new FragmentCutDofCell(presetModes[d],
                stored is { } b && b[d] != FragmentCutBc.ByPreset ? FragmentCutBc.Decode(b[d]) : null, OnCellChanged)).ToList();
            Rows.Add(new FragmentCutBcRow(id, cut, _maskedCuts.Contains(id), cells));
        }
        _loading = false;
        UpdateHint();
    }

    void OnCellChanged()
    {
        if (_loading) return;
        Save();
        UpdateHint();
    }

    void Save()
    {
        var modes = Rows.Where(r => r.Dofs.Any(d => d.IsOverride)).ToDictionary(r => r.CutId, r => r.Encode());
        _db.ReplaceFragmentCutBc(_schema.Id, modes);
        CommandManager.InvalidateRequerySuggested();
    }

    void ResetOverrides()
    {
        _db.ReplaceFragmentCutBc(_schema.Id, new Dictionary<int, byte[]>());
        BuildRows();
    }

    void UpdateHint()
    {
        var parts = new List<string>();
        if (!_hasMasks && Rows.Any(r => r.Dofs.Any(d => FragmentCutBc.NeedsMask(d.Effective))))
            parts.Add(Loc.S("FragmentCutBcMasksMissingHint"));
        if (_oblique) parts.Add(Loc.S("FragmentCutBcObliqueHint"));
        Hint = parts.Count == 0 ? null : string.Join(" ", parts);
    }

    internal static string ModeLabel(FragmentDofMode mode) => Loc.S(mode switch
    {
        FragmentDofMode.Fixed => "FragmentDofModeFixed",
        FragmentDofMode.Free => "FragmentDofModeFree",
        FragmentDofMode.MaskDisplacement => "FragmentDofModeMaskDisplacement",
        _ => "FragmentDofModeMaskForce",
    });

    static string PresetLabel(FragmentBcPreset preset) => Loc.S(preset switch
    {
        FragmentBcPreset.Kinematic => "FragmentBcPresetKinematic",
        FragmentBcPreset.KinematicTranslations => "FragmentBcPresetKinematicTranslations",
        FragmentBcPreset.Static => "FragmentBcPresetStatic",
        _ => "FragmentBcPresetIsolation",
    });
}
