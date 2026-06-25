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
    readonly FemMember    _member;
    readonly AppViewModel _app;

    public FemMemberEditorPage(FemMember member, AppViewModel app)
    {
        _member = member;
        _app    = app;
        InitializeComponent();
        DataContext        = new FemMemberEditorVM(member, app);
        view3D.DataContext = new Fem3DVM(member, app.db);
    }

    void ViewMode_Changed(object sender, RoutedEventArgs e)
    {
        if (!IsLoaded) return;
        view3D.DataContext = rbSchema.IsChecked == true
            ? new Fem3DVM(_member, _app.db, highlightOnSchema: true)
            : new Fem3DVM(_member, _app.db);
    }
}

public class FemMemberEditorVM : ViewModelBase
{
    readonly DatabaseService _db;
    readonly AppViewModel    _app;
    readonly FemMember       _member;
    FemDesignParams          _params;

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
        }
    }

    public bool IsPlateType => PlateMemberTypes.Contains(MemberType ?? "");

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
        set { _selectedBarSection = value; OnPropertyChanged(); OnPropertyChanged(nameof(SelectedSection)); }
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
        }
    }

    ForceSet? _selectedForceSet;
    public ForceSet? SelectedForceSet
    {
        get => _selectedForceSet;
        set { _selectedForceSet = value; OnPropertyChanged(); }
    }

    public double DesignLengthX { get => _params.DesignLengthX; set { _params = _params with { DesignLengthX = value }; OnPropertyChanged(); } }
    public double DesignLengthY { get => _params.DesignLengthY; set { _params = _params with { DesignLengthY = value }; OnPropertyChanged(); } }
    public double MuX           { get => _params.MuX;           set { _params = _params with { MuX = value };           OnPropertyChanged(); } }
    public double MuY           { get => _params.MuY;           set { _params = _params with { MuY = value };           OnPropertyChanged(); } }
    public double BetaM         { get => _params.BetaM;         set { _params = _params with { BetaM = value };         OnPropertyChanged(); } }
    public double GammaM        { get => _params.GammaM;        set { _params = _params with { GammaM = value };        OnPropertyChanged(); } }

    public ICommand SaveCommand { get; }

    public FemMemberEditorVM(FemMember member, AppViewModel app)
    {
        _member = member;
        _app    = app;
        _db     = app.db;
        _params = FemDesignParams.Parse(member.DesignParamsJson);
        _selectedBarSection   = app.CrossSections.FirstOrDefault(s => s.Id == member.CrossSectionId);
        _selectedPlateSection = app.PlateSections.FirstOrDefault(s => s.Id == member.PlateSectionId);
        _selectedForceSet     = app.ForceSets.FirstOrDefault(f => f.Id == member.ForceSetId);
        SaveCommand = new RelayCommand(_ => Save());
    }

    void Save()
    {
        _member.CrossSectionId   = IsPlateType ? null : _selectedBarSection?.Id;
        _member.PlateSectionId   = IsPlateType ? _selectedPlateSection?.Id : null;
        _member.ForceSetId       = _selectedForceSet?.Id;
        _member.DesignParamsJson = _params.ToJson();
        _db.SaveFemMember(_member);
    }
}
