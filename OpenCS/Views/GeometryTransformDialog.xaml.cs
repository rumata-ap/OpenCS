using System.Windows;
using OpenCS.Utilites;
using OpenCS.Views.Helpers;

namespace OpenCS.Views;

public enum GeometryTransformKind { Move, Scale, Rotate }

public partial class GeometryTransformDialog : Window
{
    public double Dx { get; private set; }
    public double Dy { get; private set; }
    public double Factor { get; private set; } = 1;
    public double AngleDeg { get; private set; }

    readonly GeometryTransformKind _kind;

    public GeometryTransformDialog(GeometryTransformKind kind, string titleResourceKey)
    {
        InitializeComponent();
        _kind = kind;
        Title = (string)FindResource(titleResourceKey);

        movePanel.Visibility = kind == GeometryTransformKind.Move ? Visibility.Visible : Visibility.Collapsed;
        scalePanel.Visibility = kind == GeometryTransformKind.Scale ? Visibility.Visible : Visibility.Collapsed;
        rotatePanel.Visibility = kind == GeometryTransformKind.Rotate ? Visibility.Visible : Visibility.Collapsed;
    }

    void Ok_Click(object sender, RoutedEventArgs e)
    {
        // Проверяются только поля показанного вида; нераспознанное — ошибка у поля, а не молчаливый 0
        // (масштаб 0 схлопывал контур в точку).
        FieldCheck.Clear(dxBox, dyBox, factorBox, angleBox);
        switch (_kind)
        {
            case GeometryTransformKind.Move:
                if (!FieldCheck.Read(this, dxBox, "GeometryTransformDx", out double dx)
                    || !FieldCheck.Read(this, dyBox, "GeometryTransformDy", out double dy)) return;
                Dx = dx; Dy = dy;
                break;
            case GeometryTransformKind.Scale:
                if (!FieldCheck.Read(this, factorBox, "GeometryTransformFactor", out double k, FieldCheck.Positive)) return;
                Factor = k;
                break;
            default:
                if (!FieldCheck.Read(this, angleBox, "GeometryTransformAngleDeg", out double a)) return;
                AngleDeg = a;
                break;
        }
        DialogResult = true;
    }

    void Cancel_Click(object sender, RoutedEventArgs e) => DialogResult = false;
}
