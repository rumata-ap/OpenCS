using System.Windows;

namespace OpenCS.Views;

public partial class FemMemberDialog : Window
{
    public string MemberTag  { get; set; } = "";
    public string MemberType { get; set; } = "";
    public string Range      { get; set; } = "";

    /// <summary>Предопределённые типы конструктивных элементов; пользователь может ввести произвольный.</summary>
    public string[] MemberTypes { get; } =
    [
        "Балка", "Колонна", "Плита", "Стена", "Ферма", "Раскос", "Связь", "Другое"
    ];

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
