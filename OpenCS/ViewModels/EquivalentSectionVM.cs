using System.Collections.ObjectModel;
using System.Windows.Input;
using CScore.PlateStrip;
using OpenCS.Utilites;

namespace OpenCS.ViewModels;

/// <summary>ViewModel страницы контроля эквивалентного сечения.</summary>
public sealed class EquivalentSectionVM : ViewModelBase
{
    public EquivalentSection Model { get; }
    public AppViewModel App { get; }
    public ObservableCollection<EquivalentSectionMatrixRow> MatrixRows { get; } = [];
    public IReadOnlyList<CScore.Fem.FemValidationDiagnostic> Diagnostics => Model.Diagnostics;

    public string Tag => Model.Tag;
    public string StatusText => !Model.IsCalculable
        ? Loc.S("EquivalentSectionStatusError")
        : Model.IsStale
            ? Loc.S("EquivalentSectionStatusStale")
            : Loc.S("EquivalentSectionStatusValid");
    public string PolicyText => Model.ReductionPolicy switch
    {
        ReductionPolicy.DirectUniaxial => Loc.S("EquivalentSectionPolicyDirectUniaxial"),
        ReductionPolicy.ConstitutiveIntegration => Loc.S("EquivalentSectionPolicyConstitutiveIntegration"),
        ReductionPolicy.EnergyEquivalent => Loc.S("EquivalentSectionPolicyEnergyEquivalent"),
        _ => Model.ReductionPolicy.ToString()
    };

    /// <summary>Политики для выбора на странице (порядок совпадает с <see cref="PolicyIndex"/>).</summary>
    public IReadOnlyList<string> PolicyNames { get; } =
    [
        Loc.S("EquivalentSectionPolicyDirectUniaxial"),
        Loc.S("EquivalentSectionPolicyConstitutiveIntegration"),
        Loc.S("EquivalentSectionPolicyEnergyEquivalent")
    ];

    static readonly ReductionPolicy[] PolicyOrder =
        [ReductionPolicy.DirectUniaxial, ReductionPolicy.ConstitutiveIntegration, ReductionPolicy.EnergyEquivalent];

    /// <summary>Выбранная политика редукции; применяется при «Пересчитать».</summary>
    public int PolicyIndex
    {
        get => Math.Max(0, Array.IndexOf(PolicyOrder, Model.ReductionPolicy));
        set
        {
            if (value < 0 || value >= PolicyOrder.Length) return;
            Model.ReductionPolicy = PolicyOrder[value];
            OnPropertyChanged();
            OnPropertyChanged(nameof(PolicyText));
        }
    }

    /// <summary>Формулировка Тимошенко (кручение и поперечный сдвиг); применяется при «Пересчитать».</summary>
    public bool IsTimoshenko
    {
        get => Model.BeamFormulation == StripBeamFormulation.Timoshenko;
        set
        {
            Model.BeamFormulation = value ? StripBeamFormulation.Timoshenko : StripBeamFormulation.EulerBernoulli;
            OnPropertyChanged();
            OnPropertyChanged(nameof(NoTorsionNoticeVisibility));
        }
    }

    public System.Windows.Visibility NoTorsionNoticeVisibility =>
        IsTimoshenko ? System.Windows.Visibility.Collapsed : System.Windows.Visibility.Visible;
    public string SourceKindText => Model.SourceKind switch
    {
        EquivalentSectionSourceKind.PlateSectionTangentSnapshot => Loc.S("EquivalentSectionSourceKindSnapshot"),
        _ => Model.SourceKind.ToString()
    };
    public int SourceSchemaId => Model.SourceSchemaId;
    public int SourceRegionId => Model.SourceRegionId;
    public int SourcePlateSectionId => Model.SourcePlateSectionId;
    public double WidthM => Model.Strip.ExplicitWidthM;
    public double LengthM => Model.Strip.Geometry.LengthM;
    public int WidthIntegrationPoints => Model.WidthIntegrationPoints;
    public double SpanStationFraction
    {
        get => Model.SpanStationFraction;
        set { Model.SpanStationFraction = value; OnPropertyChanged(); }
    }
    public double EA => Model.EA;
    public double EIy => Model.EIy;
    public double EIz => Model.EIz;
    public double GJ => Model.TorsionalStiffness;
    public double GAy => Model.ShearStiffnessGAy;
    public double GAz => Model.ShearStiffnessGAz;

    public ICommand RecalculateCommand { get; }
    public ICommand DeleteCommand { get; }

    public EquivalentSectionVM(EquivalentSection model, AppViewModel app)
    {
        Model = model ?? throw new ArgumentNullException(nameof(model));
        App = app ?? throw new ArgumentNullException(nameof(app));
        RecalculateCommand = new RelayCommand(_ => App.RecalculateEquivalentSectionCommand.Execute(Model));
        DeleteCommand = new RelayCommand(_ => App.DeleteEquivalentSectionCommand.Execute(Model));

        string[] rowKeys = [
            "EquivalentSectionMatrixRow0",
            "EquivalentSectionMatrixRow1",
            "EquivalentSectionMatrixRow2"
        ];
        for (int i = 0; i < 3; i++)
            MatrixRows.Add(new EquivalentSectionMatrixRow(
                Loc.S(rowKeys[i]), Model.BeamTangent[i, 0], Model.BeamTangent[i, 1], Model.BeamTangent[i, 2]));
    }
}

/// <summary>Строка матрицы жёсткости эквивалентного сечения.</summary>
public sealed record EquivalentSectionMatrixRow(string Label, double K0, double K1, double K2);
