using System.Windows;
using CScore.Fem;
using OpenCS.Services;

namespace OpenCS.Views;

/// <summary>
/// Линейная связь на гранях пластин: закрепления по шести направлениям общих осей, поворот кромки и погонные
/// пружины (кН/м², кН·м/рад на метр). Грани с разными связями — «разные» поля: нетронутые остаются у каждой грани
/// своими, итог грани — <see cref="Build"/> (Н/м², Н·м/рад/м); запись — командой редактора у вызывающего.
/// </summary>
public partial class FemLineSupportDialog : Window
{
    bool?[] _fix = new bool?[6];
    double?[] _k = new double?[6];
    bool? _edgeRotation;

    /// <summary>Параметры связи для граней с общими значениями (без «разных»).</summary>
    public FemLineSupport Result => Build(null);

    /// <param name="header">Что правится: выбранные грани или тип связи из дерева.</param>
    /// <param name="edgesText">Перечень граней (КонЭ, сторона) и их общая длина.</param>
    /// <param name="current">Связи граней (null — у грани связи нет); все без связи — шарнирное опирание пластины.</param>
    public FemLineSupportDialog(string header, string edgesText, IReadOnlyList<FemLineSupport?> current)
    {
        InitializeComponent();
        headerText.Text = header;
        this.edgesText.Text = edgesText;
        editor.SetUnits(perMetre: true);
        edgeRotationCheck.Click += (_, _) => edgeRotationCheck.IsThreeState = false;
        if (current.All(s => s == null))
        {
            ShowPreset(new FemLineSupport { Mask = 0b111, FixEdgeRotation = true });
            return;
        }
        var items = current.Select(s => s ?? new FemLineSupport()).ToList();
        var (fix, k) = FemBoundaryAuthoring.Common(items.Select(s => (s.Mask, (IReadOnlyList<double>)s.Stiffnesses)).ToList());
        editor.Show(fix, k);
        var rotations = items.Select(s => s.FixEdgeRotation).Distinct().ToList();
        edgeRotationCheck.IsThreeState = rotations.Count > 1;
        edgeRotationCheck.IsChecked = rotations.Count == 1 ? rotations[0] : null;
    }

    /// <summary>Одна связь (тип из дерева); null — шарнирное опирание пластины.</summary>
    public FemLineSupportDialog(string header, string edgesText, FemLineSupport? current)
        : this(header, edgesText, [current])
    {
    }

    void ShowPreset(FemLineSupport s)
    {
        editor.Show(s.Mask, s.Stiffnesses);
        edgeRotationCheck.IsThreeState = false;
        edgeRotationCheck.IsChecked = s.FixEdgeRotation;
    }

    // Край плиты / низ стены: X Y Z и поворот кромки — жёсткое шарнирное опирание пластины Миндлина.
    void PresetPlate_Click(object sender, RoutedEventArgs e) => ShowPreset(new FemLineSupport { Mask = 0b111, FixEdgeRotation = true });
    void PresetHinge_Click(object sender, RoutedEventArgs e) => ShowPreset(new FemLineSupport { Mask = 0b111 });
    void PresetFixed_Click(object sender, RoutedEventArgs e) => ShowPreset(new FemLineSupport { Mask = FemBoundaryDofs.All });
    void PresetNone_Click(object sender, RoutedEventArgs e) => ShowPreset(new FemLineSupport());

    /// <summary>Связь грани по значениям окна поверх прежней (<paramref name="old"/>; null — связи не было).</summary>
    public FemLineSupport Build(FemLineSupport? old)
    {
        var (mask, k) = FemBoundaryAuthoring.Apply(old?.Mask ?? 0, old?.Stiffnesses ?? new double[6], _fix, _k);
        var result = new FemLineSupport { Mask = mask, FixEdgeRotation = _edgeRotation ?? old?.FixEdgeRotation ?? false };
        result.SetStiffnesses(k);
        return result;
    }

    void Ok_Click(object sender, RoutedEventArgs e)
    {
        if (!editor.TryRead(out _fix, out _k)) return;
        _edgeRotation = edgeRotationCheck.IsChecked;
        DialogResult = true;
    }
}
