using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows.Input;
using CScore;
using OpenCS.LoadHistory;
using OpenCS.Utilites;
using OpenCS.Views;
using OpenCS.Views.Helpers;

namespace OpenCS.ViewModels;

/// <summary>Строка части в редакторе стадийного сечения.</summary>
public sealed class StagedPartRowVM : ViewModelBase
{
    public StagedPartRowVM(SectionPart part, AppViewModel app)
    {
        Part = part;
        App = app;
        Reload();
    }

    public SectionPart Part { get; }
    public AppViewModel App { get; }

    /// <summary>Наименование части.</summary>
    public string Tag
    {
        get => Part.Tag;
        set { Part.Tag = value; OnPropertyChanged(); OnPropertyChanged(nameof(Title)); App.MarkDirty(SaveCategory.CrossSections); }
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
        foreach (var a in Part.Areas) Areas.Add(new MaterialAreaVM(a, App));
        OnPropertyChanged(nameof(Number));
        OnPropertyChanged(nameof(Title));
        OnPropertyChanged(nameof(IsLinked));
        OnPropertyChanged(nameof(IsOwn));
        OnPropertyChanged(nameof(SourceText));
    }
}

/// <summary>
/// Редактор стадийного сечения: очередь частей (свои области или ссылка на обычное сечение),
/// области выбранной части и превью с подсветкой этой части.
/// </summary>
public sealed class StagedSectionVM : ViewModelBase
{
    readonly StagedSection _model;
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
        set { _model.Tag = value; OnPropertyChanged(); }
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

    void ReloadParts(SectionPart? select = null)
    {
        Parts.Clear();
        foreach (var p in _model.Parts) Parts.Add(new StagedPartRowVM(p, App));
        if (select != null)
            SelectedPart = Parts.FirstOrDefault(r => r.Part == select);
    }

    void Changed(SectionPart? select)
    {
        ReloadParts(select);
        RefreshPlot();
        App.MarkDirty(SaveCategory.CrossSections);
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
        var avm = new MaterialAreaVM(area, App);
        avm.PropertyChanged += OnAreaPropertyChanged;
        row.Areas.Add(avm);
        SelectedArea = avm;
        RefreshPlot();
        App.MarkDirty(SaveCategory.CrossSections);
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
        App.MarkDirty(SaveCategory.CrossSections);
    }

    void OpenMeshForArea(MaterialAreaVM? avm)
    {
        if (avm == null) return;
        if (_model.Id == 0) Save();
        var dlg = new MeshDialog(avm.Model, App) { Owner = System.Windows.Application.Current.MainWindow };
        if (dlg.ShowDialog() == true)
        {
            avm.RefreshPlot();
            RefreshPlot();
        }
    }

    /// <summary>Сохраняет сечение и добавляет его в проект, если оно новое.</summary>
    public void Save()
    {
        if (_model.Num == 0)
            _model.Num = App.CrossSections.Count > 0 ? App.CrossSections.Max(s => s.Num) + 1 : 1;
        StagedPartsEditor.Normalize(_model);
        App.db.SaveCrossSection(_model);
        if (!App.CrossSections.Contains(_model))
        {
            App.CrossSections.Add(_model);
            App.RefreshSectionLiveCollections();
        }
        App.MarkDirty(SaveCategory.CrossSections);
    }
}
