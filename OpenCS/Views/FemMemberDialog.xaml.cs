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

    /// <param name="showRange">Строка номеров КЭ — у групп КЭ; группе КонЭ состав задаёт выделение.</param>
    public FemMemberDialog(string initialRange = "", string initialTag = "", string? initialType = null,
        bool showRange = true, string? title = null)
    {
        InitializeComponent();
        Owner      = Application.Current.MainWindow;
        Range      = initialRange;
        MemberTag  = initialTag;
        MemberType = initialType;
        if (title != null) Title = title;
        if (!showRange) RangeLabel.Visibility = RangeBox.Visibility = Visibility.Collapsed;
        DataContext = this;
        TagBox.Focus();
        TagBox.SelectAll();
    }

    void Ok_Click(object sender, RoutedEventArgs e) => DialogResult = true;
}
