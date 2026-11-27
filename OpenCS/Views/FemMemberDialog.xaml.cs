using System.Windows;
using OpenCS.Utilites;

namespace OpenCS.Views;

public partial class FemMemberDialog : Window
{
    public string MemberTag  { get; set; } = "";
    /// <summary>Код типа (<see cref="CScore.Fem.FemMemberTypes"/>); null — не задан.</summary>
    public string? MemberType { get; set; }
    public string Range      { get; set; } = "";

    /// <summary>Номера КЭ строки <see cref="Range"/> после «ОК» (пусто — строка пуста или скрыта).</summary>
    public IReadOnlyCollection<int> RangeIds { get; private set; } = [];

    readonly bool _showRange;

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
        _showRange = showRange;
        if (!showRange) RangeLabel.Visibility = RangeBox.Visibility = Visibility.Collapsed;
        DataContext = this;
        TagBox.Focus();
        TagBox.SelectAll();
    }

    void Ok_Click(object sender, RoutedEventArgs e)
    {
        if (_showRange)
        {
            if (!ElementRanges.TryParse(Range, out var ids, out string? error, allowEmpty: true))
            {
                RangeErrorText.Text = error;
                RangeErrorText.Visibility = Visibility.Visible;
                RangeBox.Focus();
                return;
            }
            RangeIds = ids;
        }
        DialogResult = true;
    }

    void RangeBox_TextChanged(object sender, System.Windows.Controls.TextChangedEventArgs e) =>
        RangeErrorText.Visibility = Visibility.Collapsed;
}
