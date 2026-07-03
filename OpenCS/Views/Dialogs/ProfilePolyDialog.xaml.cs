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
    public int NArc => int.TryParse(NArcBox.Text, out var n) ? n : 6;
    public string ContourName => NameBox.Text.Trim();
    public double? Slope
    {
        get
        {
            if (double.TryParse(SlopeBox.Text, out var val) && val > 0)
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
                SlopeHint.Text = "(для двутавров с уклоном полок по ГОСТ 8239)";
                SlopeBox.Text = $"{IBeamProfile.DefaultSlope * 100:F1}";
            }
            else
            {
                SlopeHint.Text = "(для швеллеров с уклоном полок по ГОСТ 8240)";
                var chP = (ChannelProfile)profile;
                SlopeBox.Text = $"{chP.Slope * 100:F1}";
            }
        }
        else if (bentChannel)
        {
            SlopeBox.IsEnabled = false;
            SlopeHint.Text = "(гнутый швеллер, наружные углы скруглены)";
            SlopeBox.Text = "0";
        }
        else
        {
            SlopeBox.IsEnabled = false;
            SlopeHint.Text = "(не применяется)";
            SlopeBox.Text = "0";
        }

        // Properties
        PropsBox.Text = GetProfileProps(profile);
    }

    void Ok_Click(object sender, RoutedEventArgs e)
    {
        if (ProfileCombo.SelectedIndex < 0)
        {
            MessageBox.Show("Выберите профиль из списка.", "Ошибка",
                MessageBoxButton.OK, MessageBoxImage.Warning);
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

    static string Mm(double v) => $"{v * 1000:F1}";

    static string GetProfileProps(object profile) => profile switch
    {
        IBeamProfile p =>
            $"H={Mm(p.H)} мм  B={Mm(p.B)} мм\n" +
            $"tw={Mm(p.tw)} мм  tf={Mm(p.tf)} мм\n" +
            $"R1={Mm(p.R1)} мм  r2={Mm(p.r2)} мм\n" +
            $"A={p.A:F2} см²{(p.r2 > 0 ? $"\nУклон: {IBeamProfile.DefaultSlope * 100:F0}%" : "")}",

        ChannelProfile p =>
            $"H={Mm(p.H)} мм  B={Mm(p.B)} мм{(p.Bent ? " (гнутый)" : "")}\n" +
            $"tw={Mm(p.tw)} мм  tf={Mm(p.tf)} мм{(p.Slope > 0 ? $"  уклон={p.Slope * 100:F0}%" : "")}\n" +
            $"R1={Mm(p.R1)} мм  r2={Mm(p.r2)} мм  X0={p.X0 * 1000:F1} мм\n" +
            $"A={p.A:F2} см²",

        AngleProfile p =>
            $"H={Mm(p.H)} мм  Bf={Mm(p.Bf)} мм\n" +
            $"Tw={Mm(p.Tw)} мм  Tf={Mm(p.Tf)} мм\n" +
            $"R={Mm(p.R)} мм  r={Mm(p.r_)} мм\n" +
            $"A={p.A:F2} см²",

        RectTubeProfile p =>
            $"H={Mm(p.H)} мм  B={Mm(p.B)} мм\n" +
            $"t={Mm(Math.Min(p.tw, p.tf))} мм  r={Mm(p.r)} мм\n" +
            $"A={p.A:F2} см²",

        RoundTubeProfile p =>
            $"D={Mm(p.D)} мм  t={Mm(p.t)} мм\n" +
            $"A={p.A:F2} см²",

        _ => ""
    };
}
