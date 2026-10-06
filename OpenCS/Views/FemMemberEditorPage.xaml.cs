using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using CScore;
using CScore.Fem;
using OpenCS.Utilites;
using OpenCS.ViewModels;

namespace OpenCS.Views;

public partial class FemMemberEditorPage : UserControl
{
    readonly FemMemberGroup _member;
    readonly AppViewModel   _app;

    public FemMemberEditorPage(FemMemberGroup member, AppViewModel app)
    {
        _member = member;
        _app    = app;
        InitializeComponent();
        var vm = new FemMemberEditorVM(member, app);
        DataContext = vm;
        Set3D(new Fem3DVM(member, app.db));
        // Подписки на коллекции приложения и мозаику — пока страница на экране.
        Loaded += (_, _) =>
        {
            vm.Attach();
            if (view3D.DataContext is Fem3DVM v) Hook(v, true);
        };
        Unloaded += (_, _) =>
        {
            vm.Detach();
            if (view3D.DataContext is Fem3DVM v) Hook(v, false);
        };
    }

    void ViewMode_Changed(object sender, RoutedEventArgs e)
    {
        if (!IsLoaded) return;
        Set3D(rbSchema.IsChecked == true
            ? new Fem3DVM(_member, _app.db, highlightOnSchema: true)
            : new Fem3DVM(_member, _app.db));
    }

    /// <summary>3D-вид; выбор проверки в его мозаике переключает проверку в блоке параметров.</summary>
    void Set3D(Fem3DVM vm3D)
    {
        if (view3D.DataContext is Fem3DVM old) Hook(old, false);
        Hook(vm3D, true);
        view3D.DataContext = vm3D;
    }

    void Hook(Fem3DVM vm3D, bool on)
    {
        vm3D.Mosaic.PropertyChanged -= Mosaic_PropertyChanged;
        if (on) vm3D.Mosaic.PropertyChanged += Mosaic_PropertyChanged;
    }

    void Mosaic_PropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(PlateRebarMosaicVM.SelectedSubject)
            && sender is PlateRebarMosaicVM { SelectedSubject.Subject: PlateRebarMosaicVM.CheckInfo info })
            ((FemMemberEditorVM)DataContext).SelectCheck(info.Check);
    }
}

public class FemMemberEditorVM : ViewModelBase
{
    readonly DatabaseService _db;
    readonly AppViewModel    _app;
    readonly FemMemberGroup  _member;
    /// <summary>Редактор параметров СП 16 (стальные стержни).</summary>
    public SteelDesignParamsEditorVM SteelEditor { get; } = new();

    public string Tag
    {
        get => _member.Tag;
        set { _member.Tag = value; OnPropertyChanged(); }
    }

    static readonly HashSet<string> PlateMemberTypes =
        new(System.StringComparer.OrdinalIgnoreCase) { "Плита", "Стена" };

    public string? MemberType
    {
        get => _member.MemberType;
        set
        {
            _member.MemberType = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(IsPlateType));
            OnPropertyChanged(nameof(AllSections));
            OnPropertyChanged(nameof(ShowSteelParams));
        }
    }

    /// <summary>
    /// Группа ссылается на КЭ сетки импортированной схемы (ЛИРА, SCAD): вид её элементов известен по составу,
    /// тип не выбирается; набор усилий не привязывается — строки находятся по номерам КЭ.
    /// </summary>
    public bool IsMeshGroup { get; }

    /// <summary>Однородный состав группы КЭ сетки: true — только пластины, false — только стержни, null — смешанный
    /// или группа конструктивных элементов (тогда вид — по выбранному типу).</summary>
    readonly bool? _meshPlates;

    /// <summary>Тип выбирается вручную: группа конструктивных элементов или смешанная группа КЭ сетки.</summary>
    public bool CanChooseType => _meshPlates == null;

    /// <summary>Набор усилий привязывается только у групп конструктивных элементов.</summary>
    public bool CanBindForceSet => !IsMeshGroup;

    /// <summary>Состав группы КЭ сетки («120 КЭ: пластины»).</summary>
    public string Composition { get; } = "";

    public bool IsPlateType => _meshPlates ?? PlateMemberTypes.Contains(MemberType ?? "");

    public string[] MemberTypes { get; } = ["Балка", "Колонна", "Плита", "Стена", "Ферма", "Раскос", "Связь", "Другое"];

    /// <summary>Список доступных сечений — стержневые или пластинчатые в зависимости от типа.</summary>
    public System.Collections.IEnumerable AllSections => IsPlateType
        ? (System.Collections.IEnumerable)_app.PlateSections
        : _app.CrossSections;

    bool _showAllForceSets;
    public bool ShowAllForceSets
    {
        get => _showAllForceSets;
        set { _showAllForceSets = value; OnPropertyChanged(); OnPropertyChanged(nameof(AllForceSets)); }
    }

    public IEnumerable<ForceSet> AllForceSets => _showAllForceSets
        ? _app.ForceSets
        : _app.ForceSets.Where(f => f.SourceMemberId == _member.Id);

    CrossSection? _selectedBarSection;
    PlateSection? _selectedPlateSection;

    /// <summary>Выбранное стержневое сечение (когда тип — балка/колонна/ферма/...).</summary>
    public CrossSection? SelectedBarSection
    {
        get => _selectedBarSection;
        set { _selectedBarSection = value; OnPropertyChanged(); OnPropertyChanged(nameof(SelectedSection)); OnPropertyChanged(nameof(ShowSteelParams)); }
    }

    /// <summary>Выбранное пластинчатое сечение (когда тип — плита/стена).</summary>
    public PlateSection? SelectedPlateSection
    {
        get => _selectedPlateSection;
        set { _selectedPlateSection = value; OnPropertyChanged(); OnPropertyChanged(nameof(SelectedSection)); }
    }

    /// <summary>Унифицированный геттер для биндинга ComboBox.SelectedItem.</summary>
    public object? SelectedSection
    {
        get => IsPlateType ? (object?)_selectedPlateSection : _selectedBarSection;
        set
        {
            if (value is PlateSection ps) { _selectedPlateSection = ps; OnPropertyChanged(nameof(SelectedPlateSection)); }
            else if (value is CrossSection cs) { _selectedBarSection = cs; OnPropertyChanged(nameof(SelectedBarSection)); }
            else { _selectedBarSection = null; _selectedPlateSection = null; }
            OnPropertyChanged();
            OnPropertyChanged(nameof(ShowSteelParams));
        }
    }

    ForceSet? _selectedForceSet;
    public ForceSet? SelectedForceSet
    {
        get => _selectedForceSet;
        set { _selectedForceSet = value; OnPropertyChanged(); }
    }


    /// <summary>Параметры СП 16 нужны только стальным стержням: скрыты для пластин, ЖБ-сечений
    /// (есть бетонная область) и пока сечение не выбрано.</summary>
    public bool ShowSteelParams =>
        !IsPlateType && _selectedBarSection != null
        && !_selectedBarSection.Areas.Any(a =>
            (a.Material ?? _app.Materials.FirstOrDefault(m => m.Id == a.MaterialId))?.Type == MatType.Concrete);

    public ICommand SaveCommand { get; }

    // ── Параметры проверок группы по КЭ (продольный изгиб, расчётные длины) ──

    /// <summary>Блок «Продольный изгиб» выбранной проверки — тот же, что в диалоге «Нормативная проверка».</summary>
    public FemCheckBucklingVM Buckling { get; } = new();

    /// <summary>Проверки группы с блоком продольного изгиба (сталь, ЖБ стержни, ЖБ пластины по прочности).</summary>
    public IReadOnlyList<FemCheck> Checks { get; private set; } = [];

    public bool HasChecks => Checks.Count > 0;

    FemCheck? _selectedCheck;
    public FemCheck? SelectedCheck
    {
        get => _selectedCheck;
        set
        {
            _selectedCheck = value;
            OnPropertyChanged();
            if (value != null) Buckling.Load(value);
            else Buckling.NormCode = null;
        }
    }

    public ICommand SaveRunCheckCommand { get; }

    static bool HasBuckling(FemCheck c) =>
        c.NormCode is "steel_check" or "rc_check"
        || c.NormCode == "rc_plate_check" && PlateCheckParams.Parse(c.ParamsJson).CheckGroup != "sls";

    void RefreshChecks()
    {
        var selected = _selectedCheck;
        Checks = _app.FemChecks.Where(c => !c.TargetsElement && c.MemberId == _member.Id && HasBuckling(c)).ToList();
        OnPropertyChanged(nameof(Checks));
        OnPropertyChanged(nameof(HasChecks));
        SelectedCheck = selected != null && Checks.Contains(selected) ? selected : Checks.FirstOrDefault();
    }

    void FemChecks_CollectionChanged(object? sender, System.Collections.Specialized.NotifyCollectionChangedEventArgs e) =>
        RefreshChecks();

    /// <summary>Выбрать проверку (из мозаики 3D-вида); чужие проверки не выбираются.</summary>
    public void SelectCheck(FemCheck check)
    {
        if (check != _selectedCheck && Checks.Contains(check)) SelectedCheck = check;
    }

    /// <summary>Подписаться на коллекцию проверок приложения (страница на экране) и обновить список.</summary>
    public void Attach()
    {
        _app.FemChecks.CollectionChanged -= FemChecks_CollectionChanged;
        _app.FemChecks.CollectionChanged += FemChecks_CollectionChanged;
        RefreshChecks();
    }

    /// <summary>Отписаться от коллекции проверок приложения (страница скрыта).</summary>
    public void Detach() => _app.FemChecks.CollectionChanged -= FemChecks_CollectionChanged;

    void SaveCheckParams()
    {
        if (_selectedCheck == null) return;
        Buckling.Apply(_selectedCheck);
        _db.SaveFemCheck(_selectedCheck);
    }

    public FemMemberEditorVM(FemMemberGroup member, AppViewModel app)
    {
        _member = member;
        _app    = app;
        _db     = app.db;
        SteelEditor.Load(CScore.Sp16.SteelDesignParams.Parse(member.DesignParamsJson));

        // CrossSectionId — собственное поле каждого элемента, а не группы
        // (см. docs/superpowers/specs/2026-07-17-fem-constructive-member-editor-design.md) — начальный
        // выбор в комбобоксе берём с первого элемента группы, у которого сечение назначено.
        var scope = app.db.GetFemCheckScope(member);
        var primaryCrossSectionId = scope.RefersToMeshElements
            ? scope.Elements.Select(e => e.Element.CrossSectionId).FirstOrDefault(id => id != null)
            : scope.Members.Select(e => e.CrossSectionId).FirstOrDefault(id => id != null);

        IsMeshGroup = scope.RefersToMeshElements;
        if (IsMeshGroup)
        {
            int shells = scope.Elements.Count(e => e.Element.ElemType == "shell");
            int bars = scope.Elements.Count - shells;
            _meshPlates = shells > 0 && bars == 0 ? true : bars > 0 && shells == 0 ? false : null;
            Composition = string.Format(Loc.S("FemGroupComposition"), scope.Elements.Count, bars, shells);
        }

        _selectedBarSection   = app.CrossSections.FirstOrDefault(s => s.Id == primaryCrossSectionId);
        _selectedPlateSection = app.PlateSections.FirstOrDefault(s => s.Id == member.PlateSectionId);
        _selectedForceSet     = app.ForceSets.FirstOrDefault(f => f.Id == member.ForceSetId);
        SaveCommand = new RelayCommand(_ => Save());
        SaveRunCheckCommand = new RelayCommand(_ =>
        {
            if (!Save() || _selectedCheck == null) return;
            app.RunFemCheckCommand.Execute(_selectedCheck);
        });
        RefreshChecks();
    }

    /// <summary>Сохраняет группу и параметры выбранной проверки; false — параметры СП 16 с ошибкой.</summary>
    bool Save()
    {
        // Параметры СП 16 проверяются только когда они видны (стальной стержень); иначе сохраняются как были.
        CScore.Sp16.SteelDesignParams? steel = null;
        if (ShowSteelParams)
        {
            if (!SteelEditor.TryBuild(out var built, out var error))
            {
                MessageBox.Show(error, Loc.S("Warning"), MessageBoxButton.OK, MessageBoxImage.Warning);
                return false;
            }
            steel = built;
        }
        _member.PlateSectionId   = IsPlateType ? _selectedPlateSection?.Id : null;
        _member.ForceSetId       = _selectedForceSet?.Id;
        if (steel != null) _member.DesignParamsJson = steel.ToJson();
        _db.SaveFemMemberGroup(_member);

        // Сечение назначается напрямую каждому элементу группы — эта страница проставляет выбранное
        // сечение всем элементам группы разом (массовое действие, без хранения связи «группа → сечение»).
        if (!IsPlateType)
            _db.SetFemMemberGroupCrossSection(_member, _selectedBarSection?.Id);

        SaveCheckParams();
        return true;
    }
}
