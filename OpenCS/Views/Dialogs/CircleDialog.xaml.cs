using CScore;
using OpenCS.Services;
using OpenCS.Utilites;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace OpenCS.Views.Dialogs;

/// <summary>Новый круг пула: центр и радиус в метрах, проверка — <see cref="ContourAuthoring.ValidateCircle"/>.</summary>
public partial class CircleDialog : Window
{
    public double X { get; private set; }
    public double Y { get; private set; }
    public double Radius { get; private set; }

    public CircleDialog()
    {
        InitializeComponent();
        Owner = Application.Current.MainWindow;
    }

    void Ok_Click(object sender, RoutedEventArgs e)
    {
        foreach (var b in new[] { XBox, YBox, RadiusBox }) b.ClearValue(Control.BorderBrushProperty);
        if (!Read(XBox, "CircleXm", out var x) || !Read(YBox, "CircleYm", out var y) || !Read(RadiusBox, "CircleRm", out var r))
            return;
        var errors = ContourAuthoring.ValidateCircle(new CircleP(x, y, r));
        if (errors.Count > 0)
        {
            var box = errors[0].Path switch { "x" => XBox, "y" => YBox, _ => RadiusBox };
            string label = errors[0].Path switch { "x" => "CircleXm", "y" => "CircleYm", _ => "CircleRm" };
            Fail(box, label, errors[0].Message);
            return;
        }
        X = x; Y = y; Radius = r;
        DialogResult = true;
    }

    bool Read(TextBox box, string labelKey, out double value)
    {
        if (NumberInput.TryParse(box.Text, out value)) return true;
        Fail(box, labelKey, Loc.S("ContourErrNotFinite"));
        return false;
    }

    void Fail(TextBox box, string labelKey, string message)
    {
        box.BorderBrush = Brushes.Red;
        MessageBox.Show(this, $"{Loc.S(labelKey).TrimEnd(':')}: {message}", Loc.S("Warning"),
            MessageBoxButton.OK, MessageBoxImage.Warning);
        box.Focus();
        box.SelectAll();
    }
}
