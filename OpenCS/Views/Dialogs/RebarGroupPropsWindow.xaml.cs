using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;

using OpenCS.ViewModels;

namespace OpenCS.Views.Dialogs;

public partial class RebarGroupPropsWindow : Window
{
    enum Unit { mm, cm, m }

    readonly int _count;
    readonly double _area, _cx, _cy, _ix, _iy, _ixy, _dMin, _dMax;

    public RebarGroupPropsWindow(IReadOnlyList<BarItem> bars, string? tag = null)
    {
        InitializeComponent();
        Owner = Application.Current.MainWindow;

        if (tag != null)
            Title = $"Свойства группы «{tag}»";

        _count = bars.Count;

        double sumA = 0, sumAx = 0, sumAy = 0;

        foreach (var b in bars)
        {
            double a = Math.PI * b.Diameter * b.Diameter / 4;
            sumA  += a;
            sumAx += a * b.X;
            sumAy += a * b.Y;

            if (b.Diameter > _dMax) _dMax = b.Diameter;
            if (_dMin == 0 || b.Diameter < _dMin) _dMin = b.Diameter;
        }

        _area = sumA;
        _cx = sumA > 0 ? sumAx / sumA : 0;
        _cy = sumA > 0 ? sumAy / sumA : 0;

        _ix = 0; _iy = 0; _ixy = 0;
        foreach (var b in bars)
        {
            double a = Math.PI * b.Diameter * b.Diameter / 4;
            double dx = b.X - _cx;
            double dy = b.Y - _cy;
            double i0 = Math.PI * Math.Pow(b.Diameter, 4) / 64;
            _ix  += i0 + a * dy * dy;
            _iy  += i0 + a * dx * dx;
            _ixy += a * dx * dy;
        }

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
        if (CountText == null) return;
        var u = SelectedUnit;
        double lf = Lf(u), af = Af(u), inf = If(u);
        string fmt = "F" + (u == Unit.mm ? 2 : 4);

        CountText.Text = _count.ToString();
        AreaText.Text = (_area * af).ToString(fmt) + " " + Au(u);
        CentroidXText.Text = (_cx * lf).ToString(fmt) + " " + Lu(u);
        CentroidYText.Text = (_cy * lf).ToString(fmt) + " " + Lu(u);
        IxText.Text = (_ix * inf).ToString(fmt) + " " + Iu(u);
        IyText.Text = (_iy * inf).ToString(fmt) + " " + Iu(u);
        IxyText.Text = (_ixy * inf).ToString(fmt) + " " + Iu(u);
        string du = u switch { Unit.mm => "мм", Unit.cm => "см", _ => "м" };
        DiameterRangeText.Text = $"{(_dMin * lf):F1} / {(_dMax * lf):F1} {du}";
    }

    void Unit_Checked(object sender, RoutedEventArgs e) => UpdateDisplay();

    static string ValueOnly(string text) => text.Split(' ')[0];

    void CopyValue_Click(object sender, RoutedEventArgs e)
    {
        var text = (sender as FrameworkElement)?.Tag switch
        {
            "Area" => ValueOnly(AreaText.Text),
            "CentroidX" => ValueOnly(CentroidXText.Text),
            "CentroidY" => ValueOnly(CentroidYText.Text),
            "Ix" => ValueOnly(IxText.Text),
            "Iy" => ValueOnly(IyText.Text),
            "Ixy" => ValueOnly(IxyText.Text),
            _ => null
        };
        if (text != null)
            Clipboard.SetText(text);
        e.Handled = true;
    }

    void Ok_Click(object sender, RoutedEventArgs e) => DialogResult = true;
}
