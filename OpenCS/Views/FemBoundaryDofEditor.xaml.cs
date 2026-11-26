using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using OpenCS.Services;
using OpenCS.Utilites;

namespace OpenCS.Views;

/// <summary>
/// Закрепления и пружины по шести направлениям общих осей (X Y Z UX UY UZ) — общий элемент окон «Закрепления и
/// пружины…» и «Линейная связь». Жёсткости в окне — кН/м и кН·м/рад (погонные — на метр грани), наружу — Н/м и
/// Н·м/рад. «Разные» значения нескольких объектов — серый флажок и пустое поле: нетронутые остаются у каждого своими
/// (<see cref="FemBoundaryAuthoring.Apply"/>). Закреплённое направление пружину не принимает — поле недоступно.
/// </summary>
public partial class FemBoundaryDofEditor : UserControl
{
    readonly CheckBox[] _fix;
    readonly TextBox[] _k;
    readonly bool[] _mixed = new bool[6];
    bool _showing;
    string _translationalUnit = "", _rotationalUnit = "";

    public FemBoundaryDofEditor()
    {
        InitializeComponent();
        _fix = [fix0, fix1, fix2, fix3, fix4, fix5];
        _k = [k0, k1, k2, k3, k4, k5];
        for (int i = 0; i < 6; i++)
        {
            int dof = i;
            _fix[i].Click += (_, _) =>
            {
                _fix[dof].IsThreeState = false;
                if (_fix[dof].IsChecked == true) { _k[dof].Text = ""; _mixed[dof] = false; }
                UpdateEnabled();
                HideError();
            };
            _k[i].TextChanged += (_, _) =>
            {
                if (_showing) return;
                _mixed[dof] = false;
                HideError();
            };
        }
    }

    /// <summary>Подписи строк пружин с единицами: «Пружина, кН/м» / «Погонная пружина, кН/м²».</summary>
    /// <param name="perMetre">Погонные жёсткости линейной связи.</param>
    public void SetUnits(bool perMetre)
    {
        _translationalUnit = Loc.S(perMetre ? "Unit_kNperM2" : "Unit_kNperM");
        _rotationalUnit = Loc.S(perMetre ? "Unit_kNmPerRadM" : "Unit_kNmPerRad");
        string row = Loc.S(perMetre ? "FemBoundaryLineSpringRow" : "FemBoundarySpringRow");
        translationalLabel.Text = $"{row}, {_translationalUnit}";
        rotationalLabel.Text = $"{row}, {_rotationalUnit}";
    }

    /// <summary>Показывает значения: null — «разные» у выбранных объектов. Жёсткости — Н/м и Н·м/рад.</summary>
    public void Show(IReadOnlyList<bool?> fix, IReadOnlyList<double?> stiffnesses)
    {
        _showing = true;
        for (int i = 0; i < 6; i++)
        {
            _fix[i].IsThreeState = fix[i] is null;
            _fix[i].IsChecked = fix[i];
            _mixed[i] = stiffnesses[i] is null;
            _k[i].Text = stiffnesses[i] is double v && v != 0 ? (v / 1e3).ToString("0.######", CultureInfo.CurrentCulture) : "";
            _k[i].ToolTip = _mixed[i] ? Loc.S("FemBoundaryMixedValue") : null;
        }
        _showing = false;
        mixedHint.Visibility = fix.Any(f => f is null) || stiffnesses.Any(k => k is null) ? Visibility.Visible : Visibility.Collapsed;
        UpdateEnabled();
        HideError();
    }

    /// <summary>Одни значения: маска и жёсткости (Н/м, Н·м/рад).</summary>
    public void Show(int mask, IReadOnlyList<double> stiffnesses) =>
        Show(Enumerable.Range(0, 6).Select(i => (bool?)((mask & (1 << i)) != 0)).ToArray(),
            stiffnesses.Select(k => (double?)k).ToArray());

    void UpdateEnabled()
    {
        for (int i = 0; i < 6; i++) _k[i].IsEnabled = _fix[i].IsChecked != true;
    }

    /// <summary>
    /// Читает поля: закрепление и жёсткость (Н/м, Н·м/рад) по направлению, null — «разные», не тронуты. Пустое поле —
    /// пружины нет. Ошибка — текстом под таблицей с названием поля, фокус на поле; false.
    /// </summary>
    public bool TryRead(out bool?[] fix, out double?[] stiffnesses)
    {
        fix = _fix.Select(c => c.IsChecked).ToArray();
        stiffnesses = new double?[6];
        HideError();
        for (int i = 0; i < 6; i++)
        {
            if (fix[i] == true) { stiffnesses[i] = 0; continue; }
            string text = _k[i].Text.Trim();
            if (text.Length == 0)
            {
                stiffnesses[i] = _mixed[i] ? null : 0;
                continue;
            }
            string? error = !NumberInput.TryParse(text, out double v) ? Loc.S("FieldErrNumber")
                : FemBoundaryAuthoring.StiffnessError(false, v);
            if (error != null)
            {
                string unit = i < 3 ? _translationalUnit : _rotationalUnit;
                ShowError(string.Format(Loc.S("FemBoundaryErrField"), FemBoundaryAuthoring.DofNames[i], unit, error));
                _k[i].Focus();
                _k[i].SelectAll();
                return false;
            }
            stiffnesses[i] = v * 1e3;
        }
        return true;
    }

    /// <summary>Сообщение под таблицей (ошибка всего набора).</summary>
    public void ShowError(string message)
    {
        errorText.Text = message;
        errorText.Visibility = Visibility.Visible;
    }

    void HideError() => errorText.Visibility = Visibility.Collapsed;
}
