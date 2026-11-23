using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows.Input;
using CScore;
using OpenCS.LoadHistory;
using OpenCS.Services;
using OpenCS.Utilites;
using OpenCS.Views;
using OpenCS.Views.Helpers;

namespace OpenCS.ViewModels;

/// <summary>Строка части в редакторе стадийного сечения.</summary>
public sealed class StagedPartRowVM : ViewModelBase
{
    readonly Func<MaterialArea, MaterialAreaVM> _vmFor;
    readonly Action _changed;

    public StagedPartRowVM(SectionPart part, AppViewModel app, Func<MaterialArea, MaterialAreaVM> vmFor, Action changed)
    {
        Part = part;
        App = app;
        _vmFor = vmFor;
        _changed = changed;
        Reload();
    }

    public SectionPart Part { get; }
    public AppViewModel App { get; }

    /// <summary>Наименование части.</summary>
    public string Tag
    {
        get => Part.Tag;
        set { Part.Tag = value; OnPropertyChanged(); OnPropertyChanged(nameof(Title)); _changed(); }
    }

    /// <summary>Номер части (с 1) — порядок включения.</summary>
    public int Number => Part.Index + 1;

    /// <summary>Заголовок строки списка: «номер. имя».</summary>
    public string Title => $"{Number}. {Part.Tag}";

    /// <summary>Часть ссылается на обычное сечение (области правятся в нём).</summary>
    public bool IsLinked => Part.SourceSectionId != null;

    /// <summary>Части со своими областями можно править.</summary>
    public bool IsOwn => !IsLinked;

    /// <summary>Источник областей части.</summary>
    public string SourceText => IsLinked
        ? string.Format(Loc.S("StagedPartLinkedTo"), App.CrossSections.FirstOrDefault(c => c.Id == Part.SourceSectionId)?.Tag ?? "?")
        : Loc.S("StagedPartOwnAreas");

    /// <summary>Области части.</summary>
    public ObservableCollection<MaterialAreaVM> Areas { get; } = [];

    /// <summary>Пересобирает список областей и подписи после изменения части.</summary>
    public void Reload()
    {
        Areas.Clear();
        foreach (var a in Part.Areas) Areas.Add(_vmFor(a));
        OnPropertyChanged(nameof(Number));
        OnPropertyChanged(nameof(Title));
        OnPropertyChanged(nameof(IsLinked));
        OnPropertyChanged(nameof(IsOwn));
        OnPropertyChanged(nameof(SourceText));
    }
}

/// <summary>
/// Редактор стадийного сечения: очередь частей (свои области или ссылка на обычное сечение),
/// области выбранной части и превью с подсветкой этой части. В базу — только по «Сохранить» через
/// <see cref="CrossSectionAuthoring"/> (части и правленые свои области вместе); «*» и вопрос при уходе — по снимку.
/// </summary>
public sealed class StagedSectionVM : ViewModelBase, IUnsavedChanges
{
    /// <summary>Записанная часть: обозначение, источник, свои области.</summary>
    sealed record SavedPart(SectionPart Part, string Tag, int? SourceSectionId, List<MaterialArea> Areas);

    readonly StagedSection _model;
    readonly Dictionary<MaterialArea, MaterialAreaVM> _areaVMs = [];
    string _savedTag = "";
    List<SavedPart> _savedParts = [];
    StagedPartRowVM? _selectedPart;
    MaterialAreaVM? _selectedArea;
    CrossSection? _selectedSource;

    public StagedSectionVM(StagedSection model, AppViewModel app)
    {
        _model = model;
        App = app;
        if (_model.Parts.Count == 0)
            StagedPartsEditor.AddOwnPart(_model, string.Format(Loc.S("StagedPartDefaultTag"), 1));
        StagedPartsEditor.Normalize(_model);
        ReloadParts();
        MarkSaved();

        SourceCandidates = new ObservableCollection<CrossSection>(StagedPartsEditor.SourceCandidates(app.CrossSections, model));
        _selectedSource = SourceCandidates.FirstOrDefault();

        AddOwnPartCommand = new RelayCommand(_ => AddOwnPart());
        AddLinkedPartCommand = new RelayCommand(_ => AddLinkedPart(), _ => SelectedSource != null);
        RemovePartCommand = new RelayCommand(_ => RemovePart(), _ => SelectedPart != null && Parts.Count > 1);
        MoveUpCommand = new RelayCommand(_ => MovePart(-1), _ => SelectedPart != null && SelectedPart.Part.Index > 0);
        MoveDownCommand = new RelayCommand(_ => MovePart(+1), _ => SelectedPart != null && SelectedPart.Part.Index < Parts.Count - 1);
        DetachPartCommand = new RelayCommand(_ => DetachPart(), _ => SelectedPart?.IsLinked == true);
        AddAreaCommand = new RelayCommand(o => AddArea(o as MaterialArea), _ => SelectedPart?.IsOwn == true);
        RemoveAreaCommand = new RelayCommand(o => RemoveArea(o as MaterialAreaVM), _ => SelectedPart?.IsOwn == true);
        OpenMeshForAreaCommand = new RelayCommand(o => OpenMeshForArea(o as MaterialAreaVM));
        SaveCommand = new RelayCommand(_ => Save());

        SelectedPart = Parts.FirstOrDefault();
    }

    public AppViewModel App { get; }
    public StagedSection Model => _model;

    /// <summary>Наименование сечения.</summary>
    public string Tag
    {
        get => _model.Tag;
        set { _model.Tag = value; OnPropertyChanged(); Touch(); }
    }

    /// <summary>Части в порядке включения.</summary>
    public ObservableCollection<StagedPartRowVM> Parts { get; } = [];

    /// <summary>Выбранная часть (её области — в средней колонке, на превью — подсвечены).</summary>
    public StagedPartRowVM? SelectedPart
    {
        get => _selectedPart;
        set
        {
            if (_selectedPart != null)
                foreach (var a in _selectedPart.Areas) a.PropertyChanged -= OnAreaPropertyChanged;
            _selectedPart = value;
            if (_selectedPart != null)
                foreach (var a in _selectedPart.Areas) a.PropertyChanged += OnAreaPropertyChanged;
            SelectedArea = null;
            OnPropertyChanged();
            RefreshPlot();
        }
    }

    /// <summary>Выбранная область (редактор справа).</summary>
    public MaterialAreaVM? SelectedArea
    {
        get => _selectedArea;
        set { _selectedArea = value; OnPropertyChanged(); }
    }

    /// <summary>Обычные сечения, на которые может ссылаться часть.</summary>
    public ObservableCollection<CrossSection> SourceCandidates { get; }

    /// <summary>Сечение для новой части-ссылки.</summary>
    public CrossSection? SelectedSource
    {
        get => _selectedSource;
        set { _selectedSource = value; OnPropertyChanged(); }
    }

    /// <summary>Данные превью, включая единые границы всех элементов.</summary>
    public CrossSectionPlotData PlotData { get; private set; } = new([], -0.1, 0.1, -0.1, 0.1);

    public IReadOnlyList<PlotElement> PlotElements => PlotData.Elements;

    public ICommand AddOwnPartCommand { get; }
    public ICommand AddLinkedPartCommand { get; }
    public ICommand RemovePartCommand { get; }
    public ICommand MoveUpCommand { get; }
    public ICommand MoveDownCommand { get; }
    public ICommand DetachPartCommand { get; }
    public ICommand AddAreaCommand { get; }
    public ICommand RemoveAreaCommand { get; }
    public ICommand OpenMeshForAreaCommand { get; }
    public ICommand SaveCommand { get; }

    /// <summary>Перестраивает превью: области выбранной части — обычным цветом, прочие — бледнее.</summary>
    public void RefreshPlot()
    {
        _model.SyncAreas(); // ссылочные части могли измениться в сечении-источнике
        PlotData = CrossSectionPlotBuilder.Build(_model,
            _selectedPart is null ? null : new HashSet<MaterialArea>(_selectedPart.Part.Areas));
        OnPropertyChanged(nameof(PlotData));
        OnPropertyChanged(nameof(PlotElements));
    }

    void OnAreaPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(MaterialAreaVM.PlotElements))
            RefreshPlot();
    }

    MaterialAreaVM VmFor(MaterialArea area)
    {
        if (_areaVMs.TryGetValue(area, out var vm)) return vm;
        vm = new MaterialAreaVM(area, App);
        vm.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName is nameof(MaterialAreaVM.IsDirty) or "" or null) Touch();
        };
        _areaVMs[area] = vm;
        return vm;
    }

    /// <summary>Свои области частей (ссылочные правятся в сечении-источнике).</summary>
    IEnumerable<MaterialAreaVM> OwnAreaVMs() =>
        CrossSectionAuthoring.AllOwnAreas(_model).Distinct().Where(_areaVMs.ContainsKey).Select(a => _areaVMs[a]);

    /// <summary>Есть правки обозначения, частей или своих областей, не записанные в проект.</summary>
    public bool IsDirty =>
        _model.Tag != _savedTag || _model.Parts.Count != _savedParts.Count
        || _model.Parts.Where((p, i) => !SamePart(p, _savedParts[i])).Any()
        || OwnAreaVMs().Any(v => v.IsDirty);

    static bool SamePart(SectionPart p, SavedPart s) =>
        ReferenceEquals(p, s.Part) && p.Tag == s.Tag && p.SourceSectionId == s.SourceSectionId
        && (p.SourceSectionId != null || p.Areas.SequenceEqual(s.Areas));

    /// <summary>Обозначение для вопроса о сохранении.</summary>
    public string DisplayName => !string.IsNullOrWhiteSpace(_model.Tag) ? _model.Tag
        : !string.IsNullOrWhiteSpace(_savedTag) ? _savedTag : Loc.S("NewStagedSection");

    void Touch() => OnPropertyChanged(nameof(IsDirty));

    void MarkSaved()
    {
        _savedTag = _model.Tag;
        _savedParts = _model.Parts.Select(p => new SavedPart(p, p.Tag, p.SourceSectionId, [.. p.Areas])).ToList();
        Touch();
    }

    bool IUnsavedChanges.SaveChanges() => Save();

    /// <summary>Обозначение, части и правленые свои области — к записанному состоянию.</summary>
    void IUnsavedChanges.DiscardChanges()
    {
        foreach (var v in OwnAreaVMs().Where(v => v.IsDirty).ToList()) v.Discard();
        _model.Tag = _savedTag;
        _model.Parts.Clear();
        foreach (var sp in _savedParts)
        {
            sp.Part.Tag = sp.Tag;
            sp.Part.SourceSectionId = sp.SourceSectionId;
            sp.Part.Areas = sp.SourceSectionId is int src
                ? App.CrossSections.FirstOrDefault(c => c.Id == src)?.Areas ?? []
                : [.. sp.Areas];
            _model.Parts.Add(sp.Part);
        }
        StagedPartsEditor.Normalize(_model);
        _model.SyncAreas();
        ReloadParts(_model.Parts.FirstOrDefault());
        OnPropertyChanged(nameof(Tag));
        RefreshPlot();
        Touch();
    }

    void ReloadParts(SectionPart? select = null)
    {
        Parts.Clear();
        foreach (var p in _model.Parts) Parts.Add(new StagedPartRowVM(p, App, VmFor, Touch));
        if (select != null)
            SelectedPart = Parts.FirstOrDefault(r => r.Part == select);
    }

    void Changed(SectionPart? select)
    {
        ReloadParts(select);
        RefreshPlot();
        Touch();
    }

    void AddOwnPart() =>
        Changed(StagedPartsEditor.AddOwnPart(_model, string.Format(Loc.S("StagedPartDefaultTag"), _model.Parts.Count + 1)));

    void AddLinkedPart()
    {
        if (SelectedSource == null) return;
        Changed(StagedPartsEditor.AddLinkedPart(_model, SelectedSource, SelectedSource.Tag));
    }

    void RemovePart()
    {
        if (SelectedPart == null || _model.Parts.Count <= 1) return;
        int i = SelectedPart.Part.Index;
        StagedPartsEditor.Remove(_model, SelectedPart.Part);
        Changed(_model.Parts[Math.Min(i, _model.Parts.Count - 1)]);
    }

    void MovePart(int delta)
    {
        if (SelectedPart == null) return;
        var part = SelectedPart.Part;
        StagedPartsEditor.Move(_model, part, delta);
        Changed(part);
    }

    void DetachPart()
    {
        if (SelectedPart == null) return;
        var part = SelectedPart.Part;
        StagedPartsEditor.Detach(part);
        Changed(part);
    }

    void AddArea(MaterialArea? area)
    {
        var row = SelectedPart;
        if (area == null || row == null || row.IsLinked || row.Part.Areas.Contains(area)) return;
        row.Part.Areas.Add(area);
        _model.SyncAreas();
        var avm = VmFor(area);
        avm.PropertyChanged -= OnAreaPropertyChanged;
        avm.PropertyChanged += OnAreaPropertyChanged;
        row.Areas.Add(avm);
        SelectedArea = avm;
        RefreshPlot();
        Touch();
    }

    void RemoveArea(MaterialAreaVM? avm)
    {
        var row = SelectedPart;
        if (avm == null || row == null || row.IsLinked) return;
        avm.PropertyChanged -= OnAreaPropertyChanged;
        row.Part.Areas.Remove(avm.Model);
        row.Areas.Remove(avm);
        _model.SyncAreas();
        if (SelectedArea == avm) SelectedArea = null;
        RefreshPlot();
        Touch();
    }

    void OpenMeshForArea(MaterialAreaVM? avm)
    {
        if (avm == null || avm.Model.Id == 0) return;
        var dlg = new MeshDialog(avm.Model, App) { Owner = System.Windows.Application.Current.MainWindow };
        if (dlg.ShowDialog() == true)
        {
            avm.RefreshPlot();
            RefreshPlot();
        }
    }

    /// <summary>Проверяет и записывает сечение с правлеными своими областями; ошибки — в журнал.</summary>
    public bool Save()
    {
        bool isNew = _model.Id == 0;
        StagedPartsEditor.Normalize(_model);
        var dirtyAreas = OwnAreaVMs().Where(v => v.IsDirty).Select(v => v.Model).ToList();
        var errors = CrossSectionAuthoring.Save(App.db, _model, App.CalcSettings, dirtyAreas);
        if (errors.Count > 0)
        {
            foreach (var e in errors)
                App.LogService.Error(string.Format(Loc.S("SectionSaveErrorLog"), DisplayName, e.Message));
            return false;
        }
        foreach (var a in dirtyAreas) _areaVMs[a].MarkSaved();
        MarkSaved();
        App.CirclesRenumber();
        App.RefreshMaterialAreaLiveCollections();
        App.RefreshSectionLiveCollections();
        App.MarkDirty(SaveCategory.CrossSections);
        App.LogService.Info(string.Format(Loc.S(isNew ? "SectionSavedNewLog" : "SectionSavedLog"), _model.Tag));
        return true;
    }
}
