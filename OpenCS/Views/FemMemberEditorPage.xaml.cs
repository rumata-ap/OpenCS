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

    public string? MemberType
    {
        get => _member.MemberType;
        set { _member.MemberType = value; OnPropertyChanged(); }
    }

    public string[] MemberTypes { get; } = ["column", "beam", "brace", "other"];

    public ObservableCollection<CrossSection> AllSections => _app.CrossSections;

    public IEnumerable<ForceSet> AllForceSets => _app.ForceSets;

    CrossSection? _selectedSection;
    public CrossSection? SelectedSection
    {
        get => _selectedSection;
        set { _selectedSection = value; OnPropertyChanged(); }
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
        _selectedSection  = app.CrossSections.FirstOrDefault(s => s.Id == member.CrossSectionId);
        _selectedForceSet = app.ForceSets.FirstOrDefault(f => f.Id == member.ForceSetId);
        SaveCommand = new RelayCommand(_ => Save());
    }

    void Save()
    {
        _member.CrossSectionId   = _selectedSection?.Id;
        _member.ForceSetId       = _selectedForceSet?.Id;
        _member.DesignParamsJson = _params.ToJson();
        _db.SaveFemMember(_member);
    }
}
