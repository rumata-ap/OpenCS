using System.Globalization;
using OpenCS.Services;
using OpenCS.Utilites;

namespace OpenCS.ViewModels;

/// <summary>
/// Диалог «Наборы усилий по вариантам пакета» (спека 2026-10-17 «наборы по вариантам», п. 2.3): группы пакета со
/// счётчиками дошедших вариантов, типы расчёта группы загружений, охват «Выбранное» / «Вся схема», оценка числа наборов
/// и строк без чтения полей. Отметки и охват помнятся в сеансе.
/// </summary>
public sealed class FemCsfeaBatchForceSetsVM : ViewModelBase
{
    /// <summary>Свыше стольких строк — предупреждение о долгой проверке по КЭ.</summary>
    public const int LargeRowCount = 500_000;

    // Память сеанса: отмеченные группы, типы загружений, охват «Выбранное».
    static HashSet<string>? s_groups;
    static HashSet<string> s_types = ["C"];
    static bool s_useSelection = true;

    readonly int _shells, _beams, _selectedShells, _selectedBeams;

    /// <summary>Группа пакета: ключ, название, дошедшие из всех, отметка.</summary>
    public sealed class GroupItem(FemCsfeaBatchForceSetsVM owner, string key, int included, int total, string? longType = null,
        int longIncluded = 0) : ViewModelBase
    {
        bool _isChecked;
        bool _includeLong = true;

        public string Key { get; } = key;
        public string Title { get; } = FemCsfeaBatchForceSets.GroupTitle(key);
        public int Included { get; } = included;
        public int Total { get; } = total;
        public bool IsAvailable => Included > 0;
        public string Text => string.Format(CultureInfo.CurrentCulture, Loc.S("FemCsfeaBatchSetsGroup"), Title, Included, Total);

        /// <summary>Тип длительных частей группы (NL у N, CL у C); null — не считались.</summary>
        public string? LongType { get; } = longType;
        /// <summary>Дошедших вариантов с длительной частью (своя, равная полной или нулевая).</summary>
        public int LongIncluded { get; } = longIncluded;
        public bool HasLong => LongType != null;
        public bool LongEnabled => HasLong && IsChecked;
        public string LongText => string.Format(CultureInfo.CurrentCulture, Loc.S("FemCsfeaBatchSetsLong"), LongType, LongIncluded, Included);

        /// <summary>Ещё набор длительных частей с той же основой имени (связка N↔NL, C↔CL по вариантам).</summary>
        public bool IncludeLong
        {
            get => _includeLong;
            set
            {
                if (_includeLong == value) return;
                _includeLong = value;
                OnPropertyChanged();
                owner.Refresh();
            }
        }

        /// <summary>Длительный набор будет создан.</summary>
        public bool WithLong => HasLong && IncludeLong && LongIncluded > 0;

        public bool IsChecked
        {
            get => _isChecked;
            set
            {
                if (_isChecked == value || (value && !IsAvailable)) return;
                _isChecked = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(LongEnabled));
                owner.Refresh();
            }
        }
    }

    /// <summary>Тип расчёта наборов группы загружений.</summary>
    public sealed class TypeItem(FemCsfeaBatchForceSetsVM owner, string type) : ViewModelBase
    {
        bool _isChecked;

        public string Type { get; } = type;

        public bool IsChecked
        {
            get => _isChecked;
            set
            {
                if (_isChecked == value) return;
                _isChecked = value;
                OnPropertyChanged();
                owner.Refresh();
            }
        }
    }

    /// <param name="shells">Пластин и стержней в сетке результата (вся схема).</param>
    /// <param name="selection">Выбранное в 3D-виде; null — диалог открыт не из 3D-вида или выбора нет.</param>
    /// <param name="selectedShells">Из выбранных КЭ — пластин и стержней сетки результата.</param>
    public FemCsfeaBatchForceSetsVM(FemCsfeaResultSummary summary, string analysisTag, int shells, int beams,
        FemCsfeaForceSetScope? selection = null, int selectedShells = 0, int selectedBeams = 0)
    {
        ArgumentNullException.ThrowIfNull(summary);
        AnalysisTag = analysisTag;
        _shells = shells;
        _beams = beams;
        Selection = selection;
        _selectedShells = selectedShells;
        _selectedBeams = selectedBeams;

        var batch = summary.Batch ?? [];
        var keys = FemCsfeaBatchForceSets.GroupOrder.Concat(batch.Select(b => b.GroupKey).Distinct().Except(FemCsfeaBatchForceSets.GroupOrder))
            .Where(k => batch.Any(b => b.GroupKey == k));
        // Длительная часть дошедшего варианта есть: своя с полями, равная полной или нулевая.
        bool HasLongRows(FemCsfeaBatchSummary b) => summary.LongOf(b.Index) is { } p && (p.IsZero || summary.LongRecordN(p) != null);
        Groups = keys.Select(k =>
        {
            var included = batch.Where(b => b.GroupKey == k && FemCsfeaBatchForceSets.IsIncluded(b)).ToList();
            bool hasLong = FemCsfeaBatchForceSets.HasLongParts(summary, k);
            return new GroupItem(this, k, included.Count, batch.Count(b => b.GroupKey == k),
                hasLong ? CScore.Fem.Combinations.FemLongTermPair.LongType(k) : null, hasLong ? included.Count(HasLongRows) : 0);
        }).ToList();
        LoadCaseTypes = FemCsfeaBatchForceSets.LoadCaseTypes.Select(t => new TypeItem(this, t)).ToList();

        _restoring = true;
        // Первый раз — все доступные группы комбинаций (загружения — по выбору: их тип задаёт пользователь).
        foreach (var g in Groups)
            g.IsChecked = s_groups?.Contains(g.Key) ?? g.Key != Services.FemCsfeaBatchGroup.LoadCases;
        foreach (var t in LoadCaseTypes) t.IsChecked = s_types.Contains(t.Type);
        _useSelection = HasSelection && s_useSelection;
        _restoring = false;
        Refresh();
    }

    bool _restoring;

    public string AnalysisTag { get; }

    public string Title => string.Format(CultureInfo.CurrentCulture, Loc.S("FemCsfeaBatchSetsTitle"), AnalysisTag);

    public IReadOnlyList<GroupItem> Groups { get; }

    public IReadOnlyList<TypeItem> LoadCaseTypes { get; }

    /// <summary>В пакете есть группа загружений — строка выбора типов видна.</summary>
    public bool HasLoadCases => Groups.Any(g => g.Key == Services.FemCsfeaBatchGroup.LoadCases);

    public FemCsfeaForceSetScope? Selection { get; }

    public bool HasSelection => Selection != null;

    bool _useSelection;

    /// <summary>Охват — выбранное в 3D-виде (иначе вся схема).</summary>
    public bool UseSelection
    {
        get => _useSelection;
        set
        {
            if (_useSelection == value || (value && !HasSelection)) return;
            _useSelection = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(UseWholeSchema));
            Refresh();
        }
    }

    public bool UseWholeSchema
    {
        get => !_useSelection;
        set => UseSelection = !value;
    }

    public string SelectionText => Selection is not { } s ? Loc.S("FemCsfeaBatchSetsNoSelection")
        : string.Format(CultureInfo.CurrentCulture, Loc.S("FemCsfeaBatchSetsSelection"), s.Elements.Count, _selectedShells, _selectedBeams,
            string.Join(", ", s.Members.Take(5)) + (s.Members.Count > 5 ? "…" : ""));

    public string WholeSchemaText => string.Format(CultureInfo.CurrentCulture, Loc.S("FemCsfeaBatchSetsWhole"), _shells, _beams);

    /// <summary>Охват построения: выбранное или null (вся схема).</summary>
    public FemCsfeaForceSetScope? Scope => UseSelection ? Selection : null;

    /// <summary>Отмеченные доступные группы.</summary>
    public IReadOnlyList<string> CheckedGroups => Groups.Where(g => g.IsChecked && g.IsAvailable).Select(g => g.Key).ToList();

    public IReadOnlyList<string> CheckedTypes => LoadCaseTypes.Where(t => t.IsChecked).Select(t => t.Type).ToList();

    /// <summary>Отмеченные группы, у которых строится и набор длительных частей.</summary>
    public IReadOnlyList<string> LongGroups => Groups.Where(g => g.IsChecked && g.IsAvailable && g.WithLong).Select(g => g.Key).ToList();

    public int SetCount { get; private set; }

    public long RowCount { get; private set; }

    public bool IsLarge => RowCount > LargeRowCount;

    public string EstimateText { get; private set; } = "";

    /// <summary>
    /// Отмечена группа комбинаций, у которой связки N↔NL / C↔CL по вариантам нет: полная (C, N) без набора длительных
    /// частей или пользовательская длительная (CL, NL).
    /// </summary>
    public bool ShowPairWarning => Groups.Any(g => g.IsChecked && g.IsAvailable
        && (g.Key is "CL" or "NL" || g.Key is "C" or "N" && !g.WithLong));

    public bool CanCreate => SetCount > 0;

    void Refresh()
    {
        if (_restoring) return;
        int shells = UseSelection ? _selectedShells : _shells;
        int beams = UseSelection ? _selectedBeams : _beams;
        var types = CheckedTypes;
        int sets = 0;
        long rows = 0;
        var parts = new List<string>();
        foreach (var g in Groups.Where(g => g.IsChecked && g.IsAvailable))
        {
            int typeCount = g.Key == Services.FemCsfeaBatchGroup.LoadCases ? types.Count : 1;
            if (typeCount == 0) continue;
            long shellRows = (long)g.Included * shells, beamRows = 2L * g.Included * beams;
            sets += typeCount * ((shellRows > 0 ? 1 : 0) + (beamRows > 0 ? 1 : 0));
            rows += typeCount * (shellRows + beamRows);
            parts.Add(string.Format(CultureInfo.CurrentCulture, Loc.S("FemCsfeaBatchSetsEstimateGroup"), g.Title, shellRows, beamRows)
                + (typeCount > 1 ? string.Create(CultureInfo.CurrentCulture, $" ×{typeCount}") : ""));
            if (g.WithLong)
            {
                long longShells = (long)g.LongIncluded * shells, longBeams = 2L * g.LongIncluded * beams;
                sets += (longShells > 0 ? 1 : 0) + (longBeams > 0 ? 1 : 0);
                rows += longShells + longBeams;
                parts.Add(string.Format(CultureInfo.CurrentCulture, Loc.S("FemCsfeaBatchSetsEstimateGroup"), g.LongType, longShells, longBeams));
            }
        }
        SetCount = sets;
        RowCount = rows;
        EstimateText = sets == 0 ? Loc.S("FemCsfeaBatchSetsEstimateNone")
            : string.Format(CultureInfo.CurrentCulture, Loc.S("FemCsfeaBatchSetsEstimate"), sets, rows, string.Join(", ", parts));
        OnPropertyChanged(nameof(SetCount));
        OnPropertyChanged(nameof(RowCount));
        OnPropertyChanged(nameof(IsLarge));
        OnPropertyChanged(nameof(EstimateText));
        OnPropertyChanged(nameof(ShowPairWarning));
        OnPropertyChanged(nameof(CanCreate));
    }

    /// <summary>Запомнить отметки и охват на сеанс (при «Создать»).</summary>
    public void Remember()
    {
        s_groups = Groups.Where(g => g.IsChecked).Select(g => g.Key).ToHashSet();
        s_types = CheckedTypes.ToHashSet();
        if (HasSelection) s_useSelection = UseSelection;
    }

    /// <summary>Сброс памяти сеанса (тесты).</summary>
    internal static void ResetSession()
    {
        s_groups = null;
        s_types = ["C"];
        s_useSelection = true;
    }
}
