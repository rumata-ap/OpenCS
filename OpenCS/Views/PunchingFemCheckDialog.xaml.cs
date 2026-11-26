using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using CScore.Fem;
using OpenCS.Services;
using OpenCS.Utilites;
using OpenCS.ViewModels;
using OpenCS.Views.Helpers;

namespace OpenCS.Views;

/// <summary>Окно параметров проверки продавливания по МКЭ-схеме (<c>punching_check</c>).</summary>
public partial class PunchingFemCheckDialog : Window
{
    readonly PunchingFemCheckVM _vm;

    /// <param name="existing">Изменяемая проверка; null — новая.</param>
    /// <param name="schema">Схема новой проверки; null — первая в проекте.</param>
    public PunchingFemCheckDialog(DatabaseService db, FemCheck? existing, FemSchema? schema = null)
    {
        InitializeComponent();
        Owner = Application.Current?.MainWindow is { IsLoaded: true } w && w != this ? w : null;
        Cursor = Cursors.Wait;
        try { _vm = new PunchingFemCheckVM(db, existing, schema); }
        finally { Cursor = null; }
        DataContext = _vm;
        // Выбор узла поиском по номеру или после «Найти узлы» — строка в видимой части таблицы.
        _vm.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(PunchingFemCheckVM.SelectedJoint) && _vm.SelectedJoint is { } j)
                Dispatcher.BeginInvoke(() => JointsGrid.ScrollIntoView(j), System.Windows.Threading.DispatcherPriority.Background);
        };
    }

    /// <summary>Загрузка *.asp ЛИРЫ к схеме (как в меню схемы) для кнопки «Приложить *.asp…»; true — приложен.</summary>
    public Func<FemSchema, bool>? AttachLiraAsp { get => _vm.AttachLiraAsp; init => _vm.AttachLiraAsp = value; }

    /// <summary>Сечения стержней импортной схемы по жёсткостям (как в меню схемы); true — сечения назначены.</summary>
    public Func<FemSchema, bool>? CreateBarSections { get => _vm.CreateBarSections; init => _vm.CreateBarSections = value; }

    /// <summary>Наборы усилий продавливания из открытой ЛИРЫ для кнопки «Наборы из ЛИРЫ…».</summary>
    public Func<FemSchema, OpenCS.Tasks.PunchingFemCheckParams, IReadOnlyList<CScore.Sp63Punching.Fem.PunchingFemJoint>, Task>? CreateLiraForceSets
    {
        get => _vm.CreateLiraForceSets;
        init => _vm.CreateLiraForceSets = value;
    }

    /// <summary>
    /// Черновик проверки после «Сохранить» (новая — без Id, правка — копия с Id правимой). Сохранённую проверку окно не
    /// меняет: запись и сброс результата — <see cref="FemCheckAuthoring.Save"/>.
    /// </summary>
    public FemCheck? ResultCheck { get; private set; }
    /// <summary>Пользователь выбрал «Сохранить и рассчитать».</summary>
    public bool RunRequested { get; private set; }

    void Save_Click(object sender, RoutedEventArgs e)
    {
        if (!Apply()) return;
        DialogResult = true;
    }

    void SaveAndRun_Click(object sender, RoutedEventArgs e)
    {
        if (!Apply()) return;
        RunRequested = true;
        DialogResult = true;
    }

    /// <summary>Поля окна по путям ошибок службы (<see cref="FemCheckAuthoring.Validate"/>).</summary>
    Control? FieldOf(string path) => path switch
    {
        "source" => AnalysisBox,
        "punching.concrete" => ConcreteBox,
        "punching.rbtMpa" => RbtBox,
        "punching.defaultAxMm" => DefaultAxBox,
        "punching.defaultAyMm" => DefaultAyBox,
        "punching.sw" => DiameterBox,
        "punching.sw.material" => RebarBox,
        "punching.options.holeInfluence" => HoleInfluenceBox,
        "punching.angleDeg" => AngleBox,
        "punching.edgeInfluence" => EdgeInfluenceBox,
        "punching.wallRatio" => WallRatioBox,
        "punching.pile.sizeMm" => PileSizeBox,
        _ => null,
    };

    bool Apply()
    {
        // Enter с кнопкой по умолчанию не уводит фокус, у полей — задержка записи: числа переносятся до проверки.
        JointsGrid.CommitEdit(DataGridEditingUnit.Cell, true);
        JointsGrid.CommitEdit(DataGridEditingUnit.Row, true);
        foreach (var box in Descendants<TextBox>(this))
            box.GetBindingExpression(TextBox.TextProperty)?.UpdateSource();
        foreach (var box in new[] { RbtBox, DefaultAxBox, DefaultAyBox, DiameterBox, HoleInfluenceBox, AngleBox, EdgeInfluenceBox, WallRatioBox, PileSizeBox })
            FieldCheck.Clear(box);

        if (FindInvalid(this) is { } bad)
        {
            string label = FieldLabel(bad);
            MessageBox.Show(this, string.Format(Loc.S("PunchingFemFieldInvalid"), label.Length > 0 ? label : Title),
                Title, MessageBoxButton.OK, MessageBoxImage.Warning);
            FocusField(bad);
            return false;
        }
        var draft = _vm.BuildDraft();
        var errors = _vm.Validate(draft);
        if (!FemCheckDialogErrors.Show(this, errors, TagBox, TagErrorText))
        {
            // Имя уже в фокусе (текст под полем); иначе — первое поле с ошибкой: красная рамка, группа раскрыта.
            if (errors.All(e => e.Path != "tag") && errors.Select(e => FieldOf(e.Path)).FirstOrDefault(c => c != null) is { } field)
            {
                if (field is TextBox tb) tb.BorderBrush = Brushes.Red;
                FocusField(field);
            }
            return false;
        }
        ResultCheck = draft;
        return true;
    }

    void TagBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (TagErrorText != null) TagErrorText.Visibility = Visibility.Collapsed;
    }

    /// <summary>Enter в поиске узла — только переход к узлу (поиск живой), окно не сохраняется.</summary>
    void NodeSearch_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter) e.Handled = true;
    }

    /// <summary>Раскрывает свёрнутые группы над полем и ставит в него фокус.</summary>
    static void FocusField(Control field)
    {
        for (DependencyObject? d = field; d != null; d = VisualTreeHelper.GetParent(d) ?? LogicalTreeHelper.GetParent(d))
            if (d is Expander { IsExpanded: false } ex) ex.IsExpanded = true;
        field.Dispatcher.BeginInvoke(() =>
        {
            field.Focus();
            if (field is TextBox tb) tb.SelectAll();
        }, System.Windows.Threading.DispatcherPriority.Input);
    }

    /// <summary>
    /// Подпись числового поля для сообщения: заголовок столбца таблицы узлов, иначе ближайшая подпись слева в той же
    /// панели (не единица) (обозначение «ax», «d», флажок «Rbt вручную»), иначе подпись в первом столбце той же строки сетки.
    /// </summary>
    internal static string FieldLabel(TextBox box)
    {
        for (DependencyObject? d = box; d != null; d = VisualTreeHelper.GetParent(d))
            if (d is DataGridCell { Column.Header: { } header })
                return header.ToString() ?? "";
        if (VisualTreeHelper.GetParent(box) is not Panel panel) return "";
        for (int i = panel.Children.IndexOf(box) - 1; i >= 0; i--)
        {
            switch (panel.Children[i])
            {
                case CheckBox { Content: string s }: return s.Trim();
                case TextBlock t when t.Text.Length > 0 && !ReferenceEquals(t.Style, t.TryFindResource("Unit")):
                    return t.Text.Trim().TrimEnd(':');
                case TextBox: return "";
            }
        }
        if (VisualTreeHelper.GetParent(panel) is Grid grid)
        {
            int row = Grid.GetRow(panel);
            if (grid.Children.OfType<TextBlock>().FirstOrDefault(t => Grid.GetRow(t) == row && Grid.GetColumn(t) == 0) is { } lbl)
                return lbl.Text.Trim().TrimEnd(':');
        }
        return "";
    }

    static IEnumerable<T> Descendants<T>(DependencyObject root) where T : DependencyObject
    {
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            if (child is T t) yield return t;
            foreach (var x in Descendants<T>(child)) yield return x;
        }
    }

    static TextBox? FindInvalid(DependencyObject root)
    {
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            if (child is TextBox { IsVisible: true } tb && Validation.GetHasError(tb)) return tb;
            if (FindInvalid(child) is { } found) return found;
        }
        return null;
    }

    void NumBox_GotKeyboardFocus(object sender, KeyboardFocusChangedEventArgs e)
    {
        if (sender is TextBox tb) tb.SelectAll();
    }

    /// <summary>Первый щелчок по неактивному числовому полю — фокус и выделение всего текста.</summary>
    void NumBox_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (sender is TextBox { IsKeyboardFocusWithin: false } tb)
        {
            e.Handled = true;
            tb.Focus();
        }
    }
}
