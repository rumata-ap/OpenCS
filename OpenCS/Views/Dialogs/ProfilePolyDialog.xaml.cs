using CScore;
using System.Windows;
using System.Windows.Controls;
using OpenCS.Utilites;

namespace OpenCS.Views.Dialogs;

public partial class ProfilePolyDialog : Window
{
    readonly ProfileDB _pdb = new();
    List<(int Id, string Name)> _subtypes = [];
    List<(int Id, string Name)> _profiles = [];
    string _lastProfileName = "";

    public string ShapeType => TypeCombo.SelectedItem?.ToString() ?? "";
    public int ProfileId
    {
        get
        {
            int idx = ProfileCombo.SelectedIndex;
            return idx >= 0 && idx < _profiles.Count ? _profiles[idx].Id : 0;
        }
    }
    /// <summary>Наибольшее число точек на дугу скругления.</summary>
    public const int MaxArcPoints = 90;

    public int NArc => int.TryParse(NArcBox.Text, out var n) ? n : 6;
    public string ContourName => NameBox.Text.Trim();
    public double? Slope
    {
        get
        {
            if (NumberInput.TryParse(SlopeBox.Text, out var val) && val > 0)
                return val / 100.0;
            return null;
        }
    }
    public bool IsHollow => ShapeType is "Трубы" or "Прямоугольные трубы";

    public ProfilePolyDialog()
    {
        InitializeComponent();
        Owner = Application.Current.MainWindow;

        var types = _pdb.GetTypes();
        TypeCombo.ItemsSource = types;
        if (types.Count > 0)
            TypeCombo.SelectedIndex = 0;
    }

    void TypeCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (TypeCombo.SelectedItem is not string type) return;
        _subtypes = _pdb.GetSubtypes(type);
        SubTypeCombo.ItemsSource = _subtypes.Select(s => s.Name).ToList();
        if (_subtypes.Count > 0)
            SubTypeCombo.SelectedIndex = 0;
    }

    void SubTypeCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        int idx = SubTypeCombo.SelectedIndex;
        if (idx < 0 || idx >= _subtypes.Count) return;
        _profiles = _pdb.GetProfiles(_subtypes[idx].Id);
        ProfileCombo.ItemsSource = _profiles.Select(p => p.Name).ToList();
        if (_profiles.Count > 0)
            ProfileCombo.SelectedIndex = 0;
    }

    void ProfileCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        int idx = ProfileCombo.SelectedIndex;
        if (idx < 0 || idx >= _profiles.Count || TypeCombo.SelectedItem is not string type) return;

        var profile = _pdb.GetProfile(type, _profiles[idx].Id);

        string formattedName = FormatProfileName(type, GetProfileName(profile));
        if (string.IsNullOrWhiteSpace(NameBox.Text) || NameBox.Text == _lastProfileName)
            NameBox.Text = formattedName;
        _lastProfileName = formattedName;

        // Slope visibility
        bool slopeIBeam = profile is IBeamProfile ib && ib.r2 > 0;
        bool slopeChannel = profile is ChannelProfile ch && ch.Slope > 0;
        bool bentChannel = profile is ChannelProfile ch2 && ch2.Bent;

        if (slopeIBeam || slopeChannel)
        {
            SlopeBox.IsEnabled = true;
            if (slopeIBeam)
            {
                SlopeHint.Text = Loc.S("ProfileSlopeHintIBeam");
                SlopeBox.Text = $"{IBeamProfile.DefaultSlope * 100:F1}";
            }
            else
            {
                SlopeHint.Text = Loc.S("ProfileSlopeHintChannel");
                var chP = (ChannelProfile)profile;
                SlopeBox.Text = $"{chP.Slope * 100:F1}";
            }
        }
        else if (bentChannel)
        {
            SlopeBox.IsEnabled = false;
            SlopeHint.Text = Loc.S("ProfileSlopeHintBent");
            SlopeBox.Text = "0";
        }
        else
        {
            SlopeBox.IsEnabled = false;
            SlopeHint.Text = Loc.S("ProfileSlopeHintNone");
            SlopeBox.Text = "0";
        }

        // Properties
        PropsBox.Text = GetProfileProps(profile);
    }

    void Ok_Click(object sender, RoutedEventArgs e)
    {
        if (ProfileCombo.SelectedIndex < 0)
        {
            MessageBox.Show(this, Loc.S("ProfileSelectFromList"), Loc.S("Warning"),
                MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }
        if (!int.TryParse(NArcBox.Text, out int n) || n < 1 || n > MaxArcPoints)
        {
            MessageBox.Show(this, string.Format(Loc.S("ProfileArcPointsRange"), MaxArcPoints), Loc.S("Warning"),
                MessageBoxButton.OK, MessageBoxImage.Warning);
            NArcBox.Focus();
            NArcBox.SelectAll();
            return;
        }
        if (SlopeBox.IsEnabled && (!NumberInput.TryParse(SlopeBox.Text, out double s) || s < 0 || s >= 100))
        {
            MessageBox.Show(this, Loc.S("ProfileSlopeRange"), Loc.S("Warning"),
                MessageBoxButton.OK, MessageBoxImage.Warning);
            SlopeBox.Focus();
            SlopeBox.SelectAll();
            return;
        }
        DialogResult = true;
    }

    static string FormatProfileName(string shapeType, string rawName)
    {
        var map = new Dictionary<string, string>
        {
            ["Двутавры"] = "Двутавр",
            ["Швеллеры"] = "Швеллер",
            ["Уголки"] = "Уголок",
            ["Прямоугольные трубы"] = "Труба ГНЗ",
            ["Трубы"] = "Труба",
        };
        string label = map.GetValueOrDefault(shapeType, shapeType);
        return $"{label} {rawName}";
    }

    static string GetProfileName(object profile) => profile switch
    {
        IBeamProfile p => p.Name,
        ChannelProfile p => p.Name,
        AngleProfile p => p.Name,
        RectTubeProfile p => p.Name,
        RoundTubeProfile p => p.Name,
        _ => ""
    };

    static string Mm(double v) => $"{v * 1000:F1} {Loc.S("UnitMm")}";
    static string Cm2(double a) => $"{a:F2} {Loc.S("UnitCm")}²";

    /// <summary>Размеры профиля сортамента для поля «Свойства» (мм, площадь — см²).</summary>
    static string GetProfileProps(object profile) => profile switch
    {
        IBeamProfile p =>
            $"H={Mm(p.H)}  B={Mm(p.B)}\n" +
            $"tw={Mm(p.tw)}  tf={Mm(p.tf)}\n" +
            $"R1={Mm(p.R1)}  r2={Mm(p.r2)}\n" +
            $"A={Cm2(p.A)}{(p.r2 > 0 ? "\n" + string.Format(Loc.S("ProfileSlopeValue"), IBeamProfile.DefaultSlope * 100) : "")}",

        ChannelProfile p =>
            $"H={Mm(p.H)}  B={Mm(p.B)}{(p.Bent ? " " + Loc.S("ProfileBentMark") : "")}\n" +
            $"tw={Mm(p.tw)}  tf={Mm(p.tf)}{(p.Slope > 0 ? "  " + string.Format(Loc.S("ProfileSlopeValue"), p.Slope * 100) : "")}\n" +
            $"R1={Mm(p.R1)}  r2={Mm(p.r2)}  X0={Mm(p.X0)}\n" +
            $"A={Cm2(p.A)}",

        AngleProfile p =>
            $"H={Mm(p.H)}  Bf={Mm(p.Bf)}\n" +
            $"Tw={Mm(p.Tw)}  Tf={Mm(p.Tf)}\n" +
            $"R={Mm(p.R)}  r={Mm(p.r_)}\n" +
            $"A={Cm2(p.A)}",

        RectTubeProfile p =>
            $"H={Mm(p.H)}  B={Mm(p.B)}\n" +
            $"t={Mm(Math.Min(p.tw, p.tf))}  r={Mm(p.r)}\n" +
            $"A={Cm2(p.A)}",

        RoundTubeProfile p =>
            $"D={Mm(p.D)}  t={Mm(p.t)}\n" +
            $"A={Cm2(p.A)}",

        _ => ""
    };
}
