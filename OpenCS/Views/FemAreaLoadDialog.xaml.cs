using System.Windows;
using System.Windows.Controls;
using CScore.Fem;
using OpenCS.Utilites;

namespace OpenCS.Views;

/// <summary>Новая равномерная нагрузка на пластины: цель (выделенное, группа, список КЭ), давление и направление.</summary>
public partial class FemAreaLoadDialog : Window
{
    public FemAreaLoadDialog(IEnumerable<FemMemberGroup> groups)
    {
        InitializeComponent();
        groupCombo.ItemsSource = groups.ToList();
    }

    /// <summary>Добавляет нагрузку; текст ошибки оставляет диалог открытым.</summary>
    public Func<FemAreaLoadDialog, string?>? Validate { get; set; }

    public string TargetMode => targetCombo.SelectedValue as string ?? "selection";
    public FemMemberGroup? Group => groupCombo.SelectedItem as FemMemberGroup;
    public string TagsText => tagsBox.Text;
    public string Direction => directionCombo.SelectedValue as string ?? "global:z";
    public double PressurePa { get; private set; }

    void TargetChanged(object sender, SelectionChangedEventArgs e)
    {
        if (groupCombo == null) return;
        groupCombo.Visibility = TargetMode == "group" ? Visibility.Visible : Visibility.Collapsed;
        tagsBox.Visibility = TargetMode == "list" ? Visibility.Visible : Visibility.Collapsed;
    }

    void Ok_Click(object sender, RoutedEventArgs e)
    {
        if (!Pars.ParseAny(valueBox.Text, out var kpa) || kpa == 0)
        {
            ShowError(Loc.S("FemAreaLoadZero"));
            return;
        }
        PressurePa = kpa * 1e3;
        if (Validate?.Invoke(this) is { } error) { ShowError(error); return; }
        DialogResult = true;
    }

    void ShowError(string text)
    {
        errorText.Text = text;
        errorText.Visibility = Visibility.Visible;
    }
}
