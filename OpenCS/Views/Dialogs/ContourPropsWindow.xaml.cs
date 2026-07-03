using CScore;
using System.Windows;

namespace OpenCS.Views.Dialogs;

public partial class ContourPropsWindow : Window
{
    enum Unit { mm, cm, m }

    readonly double _area, _perimeter, _cx, _cy, _ix, _iy, _ixy, _ixRadius, _iyRadius;

    public ContourPropsWindow(Contour contour, string? tag = null)
    {
        InitializeComponent();
        Owner = Application.Current.MainWindow;

        if (tag != null)
            Title = $"Свойства контура '{tag}'";

        var props = new GeoProps(contour);
        _area = props.A;
        _cx = props.Centroid?.X ?? 0;
        _cy = props.Centroid?.Y ?? 0;
        _ix = props.Ix;
        _iy = props.Iy;
        _ixy = props.Ixy;

        _perimeter = 0;
        for (int i = 0; i < contour.X.Count - 1; i++)
            _perimeter += Distance(contour.X[i], contour.Y[i], contour.X[i + 1], contour.Y[i + 1]);

        _ixRadius = _area > 0 ? System.Math.Sqrt(_ix / _area) : 0;
        _iyRadius = _area > 0 ? System.Math.Sqrt(_iy / _area) : 0;

        UpdateDisplay();
    }

    Unit SelectedUnit =>
        UnitMm.IsChecked == true ? Unit.mm :
        UnitCm.IsChecked == true ? Unit.cm : Unit.m;

    static double Lf(Unit u) => u switch { Unit.mm => 1e3, Unit.cm => 1e2, _ => 1 };
    static double Af(Unit u) => u switch { Unit.mm => 1e6, Unit.cm => 1e4, _ => 1 };
    static double If(Unit u) => u switch { Unit.mm => 1e12, Unit.cm => 1e8, _ => 1 };

    static string Lu(Unit u) => u switch { Unit.mm => "мм", Unit.cm => "см", _ => "м" };
    static string Au(Unit u) => u switch { Unit.mm => "мм²", Unit.cm => "см²", _ => "м²" };
    static string Iu(Unit u) => u switch { Unit.mm => "мм⁴", Unit.cm => "см⁴", _ => "м⁴" };

    void UpdateDisplay()
    {
        if (AreaText == null) return;
        var u = SelectedUnit;
        double lf = Lf(u), af = Af(u), inf = If(u);
        string fmt = "F" + (u == Unit.mm ? 2 : 4);

        AreaText.Text = (_area * af).ToString(fmt) + " " + Au(u);
        PerimeterText.Text = (_perimeter * lf).ToString(fmt) + " " + Lu(u);
        CentroidXText.Text = (_cx * lf).ToString(fmt) + " " + Lu(u);
        CentroidYText.Text = (_cy * lf).ToString(fmt) + " " + Lu(u);
        IxText.Text = (_ix * inf).ToString(fmt) + " " + Iu(u);
        IyText.Text = (_iy * inf).ToString(fmt) + " " + Iu(u);
        IxyText.Text = (_ixy * inf).ToString(fmt) + " " + Iu(u);
        IxRadiusText.Text = (_ixRadius * lf).ToString(fmt) + " " + Lu(u);
        IyRadiusText.Text = (_iyRadius * lf).ToString(fmt) + " " + Lu(u);
    }

    void Unit_Checked(object sender, RoutedEventArgs e) => UpdateDisplay();

    static string ValueOnly(string text) => text.Split(' ')[0];

    void CopyValue_Click(object sender, RoutedEventArgs e)
    {
        var text = (sender as FrameworkElement)?.Tag switch
        {
            "Area" => ValueOnly(AreaText.Text),
            "Perimeter" => ValueOnly(PerimeterText.Text),
            "CentroidX" => ValueOnly(CentroidXText.Text),
            "CentroidY" => ValueOnly(CentroidYText.Text),
            "Ix" => ValueOnly(IxText.Text),
            "Iy" => ValueOnly(IyText.Text),
            "Ixy" => ValueOnly(IxyText.Text),
            "IxRadius" => ValueOnly(IxRadiusText.Text),
            "IyRadius" => ValueOnly(IyRadiusText.Text),
            _ => null
        };
        if (text != null)
            Clipboard.SetText(text);
        e.Handled = true;
    }

    void CopyAll_Click(object sender, RoutedEventArgs e)
    {
        string s = string.Join("\t",
            ValueOnly(AreaText.Text),
            ValueOnly(PerimeterText.Text),
            ValueOnly(CentroidXText.Text),
            ValueOnly(CentroidYText.Text),
            ValueOnly(IxText.Text),
            ValueOnly(IyText.Text),
            ValueOnly(IxyText.Text),
            ValueOnly(IxRadiusText.Text),
            ValueOnly(IyRadiusText.Text));
        Clipboard.SetText(s);
    }

    static double Distance(double x1, double y1, double x2, double y2)
        => System.Math.Sqrt((x2 - x1) * (x2 - x1) + (y2 - y1) * (y2 - y1));

    void Ok_Click(object sender, RoutedEventArgs e) => DialogResult = true;
}
