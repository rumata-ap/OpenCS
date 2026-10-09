using System.Globalization;
using System.Windows;
using System.Windows.Input;
using OpenCS.Utilites;

namespace OpenCS.Views;

/// <summary>
/// Шаг сетки конструктивных элементов: общий шаг схемы или свой (локальный, важнее общего). Для стержня — целевая
/// длина КЭ, для пластины — размер КЭ области.
/// </summary>
public partial class FemMemberMeshStepDialog : Window
{
    /// <summary>Выбранный локальный шаг, м; null — общий шаг схемы. Задан после OK.</summary>
    public double? StepM { get; private set; }

    /// <param name="target">Что меняется: «Стержень С1», «Пластины: 3».</param>
    /// <param name="commonText">Подпись общего шага: «Общий шаг схемы (0,5 м)».</param>
    /// <param name="localStepM">Текущий локальный шаг (общий у всех выбранных), null — общий или разный.</param>
    public FemMemberMeshStepDialog(string target, string commonText, double? localStepM)
    {
        InitializeComponent();
        targetText.Text = target;
        commonRadio.Content = commonText;
        if (localStepM is double step)
        {
            localRadio.IsChecked = true;
            stepBox.Text = step.ToString("G", CultureInfo.CurrentCulture);
        }
        else commonRadio.IsChecked = true;
        Loaded += (_, _) => (localRadio.IsChecked == true ? stepBox : (UIElement)commonRadio).Focus();
    }

    void StepBox_GotKeyboardFocus(object sender, KeyboardFocusChangedEventArgs e) => localRadio.IsChecked = true;

    void Apply_Click(object sender, RoutedEventArgs e)
    {
        if (localRadio.IsChecked == true)
        {
            if (!Pars.ParseAny(stepBox.Text, out var value) || !double.IsFinite(value) || value <= 0)
            {
                MessageBox.Show(this, Loc.S("FemMemberMeshStepInvalid"), Title, MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
            StepM = value;
        }
        else StepM = null;
        DialogResult = true;
    }
}
