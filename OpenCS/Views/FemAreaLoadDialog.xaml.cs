using System.Windows;
using System.Windows.Controls;
using CScore.Fem;
using OpenCS.Utilites;

namespace OpenCS.Views;

/// <summary>Новая равномерная нагрузка на пластины: цель (выделенное, группа, список КЭ), давление и направление.</summary>
public partial class FemAreaLoadDialog : Window
{
    /// <param name="loadCaseTag">Активное загружение — в него идёт нагрузка; null — загружения нет.</param>
    public FemAreaLoadDialog(IEnumerable<FemMemberGroup> groups, string? loadCaseTag)
    {
        InitializeComponent();
        groupCombo.ItemsSource = groups.ToList();
        if (loadCaseTag != null) caseText.Text = string.Format(Loc.S("FemAreaLoadCase"), loadCaseTag);
        else { caseText.Visibility = Visibility.Collapsed; noCaseText.Visibility = Visibility.Visible; }
        Loaded += (_, _) => { valueBox.Focus(); valueBox.SelectAll(); };
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
        // Нераспознанное — «нужно число», ноль — «ненулевое давление»: ошибка у поля.
        Helpers.FieldCheck.Clear(valueBox);
        if (!Helpers.FieldCheck.Read(this, valueBox, "FemAreaLoadValue", out var kpa, Helpers.FieldCheck.NonZero)) return;
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
