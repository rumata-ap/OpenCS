using System.Windows;

namespace OpenCS.Views;

public partial class FemMemberDialog : Window
{
    public string MemberTag  { get; set; } = "";
    /// <summary>Код типа (<see cref="CScore.Fem.FemMemberTypes"/>); null — не задан.</summary>
    public string? MemberType { get; set; }
    public string Range      { get; set; } = "";

    /// <summary>Типы групп: код и локализованное имя.</summary>
    public IReadOnlyList<Converters.FemMemberTypeOption> MemberTypes { get; } = Converters.FemMemberTypeOption.All();

    public FemMemberDialog(string initialRange = "")
    {
        InitializeComponent();
        Owner     = Application.Current.MainWindow;
        DataContext = this;
        Range     = initialRange;
        TagBox.Focus();
    }

    void Ok_Click(object sender, RoutedEventArgs e) => DialogResult = true;
}
