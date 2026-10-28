using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Media;

namespace OpenCS.Views;

/// <summary>
/// Вкладка «ГУ разрезов» фрагмента: шесть столбцов режимов DOF (<c>Ux … Rz</c>) — ComboBox ячейки
/// <see cref="ViewModels.FragmentCutDofCell"/>; правка поверх пресета выделена полужирным.
/// </summary>
public partial class FragmentCutBcPanel : UserControl
{
    static readonly string[] Dofs = ["Ux", "Uy", "Uz", "Rx", "Ry", "Rz"];

    public FragmentCutBcPanel()
    {
        InitializeComponent();
        foreach (var dof in Dofs) grid.Columns.Add(DofColumn(dof));
    }

    static DataGridTemplateColumn DofColumn(string dof)
    {
        var style = new Style(typeof(ComboBox));
        var overridden = new DataTrigger { Binding = new Binding($"{dof}.IsOverride"), Value = true };
        overridden.Setters.Add(new Setter(Control.FontWeightProperty, FontWeights.Bold));
        overridden.Setters.Add(new Setter(Control.ForegroundProperty, Brushes.DarkBlue));
        style.Triggers.Add(overridden);

        var combo = new FrameworkElementFactory(typeof(ComboBox));
        combo.SetBinding(ItemsControl.ItemsSourceProperty, new Binding($"{dof}.Options"));
        combo.SetBinding(Selector.SelectedItemProperty,
            new Binding($"{dof}.Selected") { Mode = BindingMode.TwoWay, UpdateSourceTrigger = UpdateSourceTrigger.PropertyChanged });
        combo.SetValue(ItemsControl.DisplayMemberPathProperty, "Label");
        combo.SetValue(FrameworkElement.StyleProperty, style);
        combo.SetValue(FrameworkElement.MinWidthProperty, 150.0);

        var header = new TextBlock();
        header.SetResourceReference(TextBlock.TextProperty, "FemResultNodal" + dof);
        return new DataGridTemplateColumn { Header = header, CellTemplate = new DataTemplate { VisualTree = combo } };
    }
}
