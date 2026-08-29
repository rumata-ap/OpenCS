using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Data;
using System.Windows.Input;

using CScore;
using CScore.Sp63Shear;
using OpenCS.Utilites;

namespace OpenCS.ViewModels;

/// <summary>ViewModel страницы задания группы поперечного армирования.</summary>
public sealed class StirrupGroupVM : ViewModelBase
{
    const double DefaultSpacingM = 0.2;
    const double DefaultOffsetM = 0.03;
    const double DefaultDiameterM = 0.008;

    readonly StirrupGroup _group;
    readonly ObservableCollection<StirrupElementVM> _elements = [];
    MaterialArea? _selectedAnchorArea;
    Material? _selectedMaterial;
    StirrupElementVM? _selectedElement;
    string _tag;
    double _spacingM;
    double _offsetM;
    double _diameterM = DefaultDiameterM;
    double _cutPosition;
    StirrupCutDirection _cutDirection = StirrupCutDirection.Vertical;
    double _copyDx;
    double _copyDy;
    int _copyCount = 1;
    string? _errorMessage;

    /// <summary>Создаёт редактор группы поперечного армирования.</summary>
    public StirrupGroupVM(MaterialArea area, AppViewModel app)
    {
        EditedArea = area ?? throw new ArgumentNullException(nameof(area));
        App = app ?? throw new ArgumentNullException(nameof(app));

        _group = area.Stirrups.FirstOrDefault() ?? new StirrupGroup
        {
            MaterialId = area.MaterialId,
            SpacingM = DefaultSpacingM,
            OffsetM = DefaultOffsetM
        };
        if (area.Stirrups.Count == 0)
            area.Stirrups.Add(_group);
        if (_group.SpacingM <= 0.0) _group.SpacingM = DefaultSpacingM;
        _tag = area.Tag;
        _spacingM = _group.SpacingM;
        _offsetM = _group.OffsetM ?? DefaultOffsetM;
        _selectedMaterial = area.Material ?? App.Materials.FirstOrDefault(m => m.Id == area.MaterialId)
            ?? App.Armatures.FirstOrDefault();
        int? savedAnchorId = _group.Elements
            .Select(element => element.Source?.AnchorAreaId)
            .FirstOrDefault(id => id.HasValue);
        _selectedAnchorArea = savedAnchorId is int anchorId
            ? App.AreasLive.FirstOrDefault(areaItem => areaItem.Id == anchorId)
                ?? App.MaterialAreas.FirstOrDefault(areaItem => areaItem.Id == anchorId)
            : App.AreasLive.FirstOrDefault();

        RefreshElements();

        AddLoopCommand = new RelayCommand(_ => AddLoop());
        AddCutCommand = new RelayCommand(_ => AddCut());
        DuplicateCommand = new RelayCommand(_ => Duplicate());
        DeleteElementCommand = new RelayCommand(parameter => DeleteElement(parameter as StirrupElementVM));
        RebuildCommand = new RelayCommand(_ => Rebuild());
        SaveCommand = new RelayCommand(_ => Save());
        CancelCommand = new RelayCommand(_ => App.CurrentPage = null!);
    }

    /// <summary>Приложение-владелец.</summary>
    public AppViewModel App { get; }

    /// <summary>Редактируемая область MaterialArea.</summary>
    public MaterialArea EditedArea { get; }

    /// <summary>Группа, редактируемая на странице.</summary>
    public StirrupGroup Group => _group;

    /// <summary>Тег области.</summary>
    public string Tag { get => _tag; set { _tag = value; OnPropertyChanged(); } }

    /// <summary>Доступные материалы поперечной арматуры.</summary>
    public IReadOnlyList<Material> AvailableMaterials => App.Armatures.Count > 0
        ? App.Armatures
        : App.Materials.Where(m => m.Type is MatType.ReSteelF or MatType.ReSteelU or MatType.Steel).ToList();

    /// <summary>Доступные полигональные области-носители.</summary>
    public IReadOnlyList<MaterialArea> AvailableAnchorAreas => App.AreasLive;

    /// <summary>Материал группы.</summary>
    public Material? SelectedMaterial
    {
        get => _selectedMaterial;
        set { _selectedMaterial = value; OnPropertyChanged(); }
    }

    /// <summary>Шаг хомутов вдоль оси стержня, м.</summary>
    public double SpacingM
    {
        get => _spacingM;
        set { _spacingM = value; _group.SpacingM = value; OnPropertyChanged(); }
    }

    /// <summary>Отступ для новых параметрических элементов, м.</summary>
    public double OffsetM
    {
        get => _offsetM;
        set { _offsetM = value; _group.OffsetM = value; OnPropertyChanged(); }
    }

    /// <summary>Область-носитель для новых элементов.</summary>
    public MaterialArea? SelectedAnchorArea
    {
        get => _selectedAnchorArea;
        set { _selectedAnchorArea = value; OnPropertyChanged(); }
    }

    /// <summary>Элементы группы для отображения в таблице.</summary>
    public ObservableCollection<StirrupElementVM> Elements => _elements;

    /// <summary>Выбранный элемент.</summary>
    public StirrupElementVM? SelectedElement
    {
        get => _selectedElement;
        set { _selectedElement = value; OnPropertyChanged(); }
    }

    /// <summary>Сообщение об ошибке последней операции.</summary>
    public string? ErrorMessage
    {
        get => _errorMessage;
        private set { _errorMessage = value; OnPropertyChanged(); }
    }

    /// <summary>Направление нового среза.</summary>
    public StirrupCutDirection CutDirection
    {
        get => _cutDirection;
        set { _cutDirection = value; OnPropertyChanged(); }
    }

    /// <summary>Координата линии нового среза, м.</summary>
    public double CutPosition
    {
        get => _cutPosition;
        set { _cutPosition = value; OnPropertyChanged(); }
    }

    /// <summary>Диаметр нового стержня, м.</summary>
    public double Diameter
    {
        get => _diameterM;
        set { _diameterM = value; OnPropertyChanged(); }
    }

    /// <summary>Диаметр нового стержня, мм; внутри модели хранится в метрах.</summary>
    public double DiameterMm
    {
        get => _diameterM * 1000.0;
        set
        {
            Diameter = value / 1000.0;
            OnPropertyChanged();
        }
    }

    /// <summary>Смещение копии по X, м.</summary>
    public double CopyDx { get => _copyDx; set { _copyDx = value; OnPropertyChanged(); } }

    /// <summary>Смещение копии по Y, м.</summary>
    public double CopyDy { get => _copyDy; set { _copyDy = value; OnPropertyChanged(); } }

    /// <summary>Количество копий.</summary>
    public int CopyCount
    {
        get => _copyCount;
        set { _copyCount = Math.Max(1, value); OnPropertyChanged(); }
    }

    /// <summary>Команда добавления замкнутого хомута по отступу.</summary>
    public ICommand AddLoopCommand { get; }
    /// <summary>Команда добавления среза.</summary>
    public ICommand AddCutCommand { get; }
    /// <summary>Команда дублирования элемента.</summary>
    public ICommand DuplicateCommand { get; }
    /// <summary>Команда удаления элемента.</summary>
    public ICommand DeleteElementCommand { get; }
    /// <summary>Команда перестроения выбранного параметрического элемента.</summary>
    public ICommand RebuildCommand { get; }
    /// <summary>Команда сохранения области.</summary>
    public ICommand SaveCommand { get; }
    /// <summary>Команда выхода без сохранения.</summary>
    public ICommand CancelCommand { get; }

    void AddLoop()
    {
        ClearError();
        if (SelectedAnchorArea is null)
        {
            SetError("StirrupErrorNoAnchor");
            return;
        }

        var element = StirrupGeometryBuilder.BuildOffsetLoop(
            SelectedAnchorArea, OffsetM, Diameter, out var error);
        if (element is null)
        {
            SetBuilderError(error);
            return;
        }

        _group.Elements.Add(element);
        RefreshElements(element);
    }

    void AddCut()
    {
        ClearError();
        if (SelectedAnchorArea is null)
        {
            SetError("StirrupErrorNoAnchor");
            return;
        }

        var elements = StirrupGeometryBuilder.BuildCuts(
            SelectedAnchorArea, CutDirection, CutPosition, OffsetM, Diameter, out var error);
        if (elements.Count == 0)
        {
            SetBuilderError(error);
            return;
        }

        _group.Elements.AddRange(elements);
        RefreshElements(elements[^1]);
    }

    void Duplicate()
    {
        ClearError();
        if (SelectedElement is null)
        {
            SetError("StirrupErrorNoSelection");
            return;
        }

        int baseIndex = _group.Elements.IndexOf(SelectedElement.Element);
        if (baseIndex < 0) return;
        var copies = new List<StirrupElement>();
        for (int i = 1; i <= CopyCount; i++)
            copies.Add(StirrupGeometryBuilder.Translate(
                SelectedElement.Element, CopyDx * i, CopyDy * i, baseIndex));
        _group.Elements.AddRange(copies);
        RefreshElements(copies[^1]);
    }

    void DeleteElement(StirrupElementVM? element)
    {
        ClearError();
        element ??= SelectedElement;
        if (element is null) return;
        _group.Elements.Remove(element.Element);
        RefreshElements(_group.Elements.LastOrDefault());
    }

    void Rebuild()
    {
        ClearError();
        var selected = SelectedElement;
        if (selected?.Element.Source is not { } source || source.Kind == StirrupElementKind.Manual)
            return;

        var anchor = ResolveAnchor(source.AnchorAreaId) ?? SelectedAnchorArea;
        if (anchor is null)
        {
            SetError("StirrupErrorNoAnchor");
            return;
        }

        var replacement = BuildFromSource(source, anchor, selected.Element.BarDiameterM, out var error);
        if (replacement.Count == 0)
        {
            SetBuilderError(error);
            return;
        }

        int index = _group.Elements.IndexOf(selected.Element);
        if (index < 0) return;
        _group.Elements.RemoveAt(index);
        _group.Elements.InsertRange(index, replacement);
        RefreshElements(replacement[^1]);
    }

    IReadOnlyList<StirrupElement> BuildFromSource(StirrupElementSource source,
                                                   MaterialArea anchor,
                                                   double diameter,
                                                   out string? error)
    {
        double offset = source.OffsetM ?? OffsetM;
        if (source.Kind == StirrupElementKind.OffsetLoop)
        {
            var element = StirrupGeometryBuilder.BuildOffsetLoop(anchor, offset, diameter, out error);
            return element is null ? [] : [element];
        }

        var direction = source.Direction ?? StirrupCutDirection.Vertical;
        return StirrupGeometryBuilder.BuildCuts(
            anchor, direction, source.Position ?? 0.0, offset, diameter, out error);
    }

    MaterialArea? ResolveAnchor(int? id) => id is null
        ? null
        : App.AreasLive.FirstOrDefault(area => area.Id == id.Value)
          ?? App.MaterialAreas.FirstOrDefault(area => area.Id == id.Value);

    void Save()
    {
        ClearError();
        EditedArea.Tag = Tag;
        EditedArea.Category = AreaCategory.Stirrups;
        EditedArea.HostAreaId = null;
        if (SelectedMaterial is not null)
        {
            EditedArea.Material = SelectedMaterial;
            EditedArea.MaterialId = SelectedMaterial.Id;
            // Тип диаграммы редактор хомутов не спрашивает — определяем по материалу
            // (для ReSteelU двухлинейной диаграммы не существует).
            EditedArea.DiagrammType =
                DiagrammCompatibility.Coerce(SelectedMaterial.Type, EditedArea.DiagrammType);
        }

        _group.MaterialId = EditedArea.MaterialId;
        _group.SpacingM = SpacingM;
        _group.OffsetM = OffsetM;
        EditedArea.Stirrups.Clear();
        EditedArea.Stirrups.Add(_group);
        try
        {
            _group.ValidateFor(EditedArea);
            App.db.SaveMaterialArea(EditedArea);
            if (!App.MaterialAreas.Contains(EditedArea))
                App.MaterialAreas.Add(EditedArea);
            App.RefreshMaterialAreaLiveCollections();
            App.LogService.Info($"Группа поперечного армирования «{EditedArea.Tag}» сохранена");
        }
        catch (Exception ex)
        {
            ErrorMessage = ex.Message;
        }
    }

    void RefreshElements(StirrupElement? selected = null)
    {
        _elements.Clear();
        for (int i = 0; i < _group.Elements.Count; i++)
            _elements.Add(new StirrupElementVM(_group.Elements[i], i + 1));
        SelectedElement = selected is null
            ? _elements.LastOrDefault()
            : _elements.FirstOrDefault(vm => ReferenceEquals(vm.Element, selected));
        OnPropertyChanged(nameof(Elements));
        CollectionViewSource.GetDefaultView(_elements).Refresh();
    }

    void RefreshElementRows()
    {
        CollectionViewSource.GetDefaultView(_elements).Refresh();
        OnPropertyChanged(nameof(Elements));
    }

    void ClearError() => ErrorMessage = null;

    void SetBuilderError(string? error)
    {
        if (string.IsNullOrWhiteSpace(error))
        {
            SetError("StirrupErrorBuild");
            return;
        }

        string key = error.Contains("отверст", StringComparison.OrdinalIgnoreCase)
            ? "StirrupErrorHolesUnsupported"
            : error.Contains("пересекает", StringComparison.OrdinalIgnoreCase)
              || error.Contains("отрезк", StringComparison.OrdinalIgnoreCase)
                ? "StirrupErrorCutOutside"
                : "StirrupErrorOffsetTooLarge";
        SetError(key, error);
    }

    void SetError(string key, string? fallback = null) => ErrorMessage =
        Application.Current?.TryFindResource(key) as string ?? fallback ?? key;
}
