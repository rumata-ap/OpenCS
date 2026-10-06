using System.Windows;
using CScore.Fem;
using OpenCS.Converters;
using OpenCS.Utilites;

namespace OpenCS.Views;

/// <summary>
/// «КонЭ из КЭ»: имя элементов, вид плоских частей (авто / плита / стена) и группа КонЭ из результата.
/// Части, на которые распадается выбор, получают имена «Имя · 1», «Имя · 2»…
/// </summary>
public partial class MeshMembersDialog : Window
{
    public string MemberTag { get; set; }
    /// <summary>"plate" | "wall"; null — по положению плоской части.</summary>
    public string? PlanarKind { get; set; }
    public bool MakeGroup { get; set; }
    /// <summary>Код типа группы (<see cref="FemMemberTypes"/>); null — не задан.</summary>
    public string? GroupType { get; set; }

    public IReadOnlyList<FemMemberTypeOption> PlanarKinds { get; } =
    [
        new(null, Loc.S("FemMeshMembersKindAuto")),
        new(FemMemberTypes.Plate, Loc.S("FemMeshMembersKindPlate")),
        new(FemMemberTypes.Wall, Loc.S("FemMeshMembersKindWall")),
    ];

    public IReadOnlyList<FemMemberTypeOption> GroupTypes { get; } = FemMemberTypeOption.All();

    /// <param name="elementCount">Число КЭ, из которых собираются элементы.</param>
    /// <param name="hasShells">Есть пластины — показывается выбор вида плоских частей.</param>
    public MeshMembersDialog(int elementCount, bool hasShells, string tag, string? planarKind, bool makeGroup, string? groupType)
    {
        InitializeComponent();
        Owner = Application.Current.MainWindow;
        MemberTag = tag;
        PlanarKind = planarKind;
        MakeGroup = makeGroup;
        GroupType = groupType;
        InfoText.Text = string.Format(Loc.S("FemMeshMembersDlgInfo"), elementCount);
        if (!hasShells) KindLabel.Visibility = KindBox.Visibility = Visibility.Collapsed;
        DataContext = this;
        TagBox.Focus();
        TagBox.SelectAll();
    }

    void Ok_Click(object sender, RoutedEventArgs e)
    {
        MemberTag = MemberTag.Trim();
        if (MemberTag.Length == 0) return;
        DialogResult = true;
    }
}
