using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using OpenCS.Utilites;

namespace OpenCS.Views;

/// <summary>Общие шаги сетки схемы: стержни и пластины. Пусто — не задан (стержни делятся только узлами, пластины —
/// по размеру из области). Свой шаг КонЭ важнее общего.</summary>
public partial class FemSchemaMeshStepsDialog : Window
{
    public double? BarStepM { get; private set; }
    public double? PlateStepM { get; private set; }
    /// <summary>Создавать АЖТ в примыканиях стержней к плитам.</summary>
    public bool AutoRigidJunctions { get; private set; }
    /// <summary>Порог угла «стержень в плоскости плиты / входит в плиту», °.</summary>
    public double RigidJunctionAngleDeg { get; private set; }

    public FemSchemaMeshStepsDialog(string schemaTag, double? barStepM, double? plateStepM,
        bool autoRigidJunctions, double rigidJunctionAngleDeg)
    {
        InitializeComponent();
        schemaText.Text = schemaTag;
        barBox.Text = barStepM?.ToString("G", CultureInfo.CurrentCulture) ?? "";
        plateBox.Text = plateStepM?.ToString("G", CultureInfo.CurrentCulture) ?? "";
        rigidJunctionsBox.IsChecked = autoRigidJunctions;
        angleBox.Text = rigidJunctionAngleDeg.ToString("G", CultureInfo.CurrentCulture);
        AutoRigidJunctions = autoRigidJunctions;
        RigidJunctionAngleDeg = rigidJunctionAngleDeg;
        Loaded += (_, _) => barBox.Focus();
    }

    void Apply_Click(object sender, RoutedEventArgs e)
    {
        if (!TryRead(barBox, out var bar) || !TryRead(plateBox, out var plate))
        {
            MessageBox.Show(this, Loc.S("FemMemberMeshStepInvalid"), Title, MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }
        bool auto = rigidJunctionsBox.IsChecked == true;
        double angle = RigidJunctionAngleDeg;
        // Порог читается только при включённом флажке: выключенный флажок сохраняет прежнее значение.
        if (auto && (!Pars.ParseAny(angleBox.Text, out angle) || !double.IsFinite(angle) || angle <= 0 || angle >= 90))
        {
            MessageBox.Show(this, Loc.S("FemSchemaRigidJunctionAngleInvalid"), Title, MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }
        BarStepM = bar;
        PlateStepM = plate;
        AutoRigidJunctions = auto;
        RigidJunctionAngleDeg = angle;
        DialogResult = true;
    }

    /// <summary>Пусто — null; иначе положительное число.</summary>
    static bool TryRead(TextBox box, out double? value)
    {
        value = null;
        if (string.IsNullOrWhiteSpace(box.Text)) return true;
        if (!Pars.ParseAny(box.Text, out var v) || !double.IsFinite(v) || v <= 0) return false;
        value = v;
        return true;
    }
}
