using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using CScore.Fem.Fields;
using Microsoft.Win32;
using OpenCS.Utilites;
using OpenCS.ViewModels;

namespace OpenCS.Views;

/// <summary>Окно поля из таблицы точек: настройки слева, предпросмотр справа (спека § 5.3).</summary>
public partial class FemFieldDialog : Window
{
    readonly FemFieldDialogVM _vm;
    bool _syncing;

    public FemFieldDialog(FemFieldDialogVM vm)
    {
        InitializeComponent();
        _vm = vm;
        DataContext = vm;
        quantityBox.ItemsSource = new[]
        {
            Item(Loc.S("FemFieldQuantityC1"), FemFieldQuantities.FoundationC1),
            Item(Loc.S("FemFieldQuantitySpring"), FemFieldQuantities.Spring),
        };
        methodBox.ItemsSource = new[]
        {
            Item(Loc.S("FemFieldMethodInterpolate"), FieldMethod.Interpolate), Item(Loc.S("FemFieldMethodNearest"), FieldMethod.Nearest),
            Item(Loc.S("FemFieldMethodIdw"), FieldMethod.Idw), Item(Loc.S("FemFieldMethodMatch"), FieldMethod.Match),
        };
        SyncFromVm();
    }

    static ComboBoxItem Item(string text, object tag) => new() { Content = text, Tag = tag };

    /// <summary>Списки и выбор, не связанные привязками, — по состоянию VM (после загрузки таблицы, смены величины).</summary>
    void SyncFromVm()
    {
        _syncing = true;
        try
        {
            var headers = _vm.Headers;
            xBox.ItemsSource = headers;
            yBox.ItemsSource = headers;
            zBox.ItemsSource = new[] { Loc.S("FemFieldNoColumn") }.Concat(headers).ToList();
            xBox.SelectedIndex = _vm.XColumn < headers.Count ? _vm.XColumn : -1;
            yBox.SelectedIndex = _vm.YColumn < headers.Count ? _vm.YColumn : -1;
            zBox.SelectedIndex = _vm.ZColumn + 1;
            quantityBox.SelectedItem = quantityBox.Items.Cast<ComboBoxItem>().FirstOrDefault(i => (string)i.Tag == _vm.Quantity);
            methodBox.SelectedItem = methodBox.Items.Cast<ComboBoxItem>().FirstOrDefault(i => (FieldMethod)i.Tag == _vm.Method);
            methodBox.IsEnabled = !_vm.IsNodes;
            lengthUnitBox.SelectedItem = lengthUnitBox.Items.Cast<ComboBoxItem>()
                .FirstOrDefault(i => Math.Abs(double.Parse((string)i.Tag, CultureInfo.InvariantCulture) - _vm.LengthScale) < 1e-12);
        }
        finally { _syncing = false; }
        RefreshPreview();
    }

    void RefreshPreview()
    {
        var eval = _vm.Evaluation;
        previewCanvas.Evaluation = eval;
        var field = _vm.BuildField(0);
        previewCanvas.Points = field.Points.Select(p => new FieldPoint(field.Definition.Alignment.Apply(p.P), p.Values)).ToList();
        previewCanvas.PointsHaveZ = field.HasZ;
        previewCanvas.PlaneTolerance = field.Definition.Settings.PlaneTolerance;
        previewCanvas.NodeTargets = _vm.NodeTargets;
        patchBox.ItemsSource = _vm.PatchLabels;
        patchBox.IsEnabled = _vm.PatchLabels.Count > 1;
        if (_vm.PatchLabels.Count > 0) patchBox.SelectedIndex = 0;
        diagnosticsList.ItemsSource = eval?.Diagnostics.Select(d => d.Tags.Count > 0 ? $"{d.Message}: {d.Tags.Count}" : d.Message).ToList();
    }

    /// <summary>Правка в поле ввода с фокусом попадает в VM до действия (Enter на «OK» не снимает фокус).</summary>
    static void CommitEdits()
    {
        if (Keyboard.FocusedElement is TextBox box) box.GetBindingExpression(TextBox.TextProperty)?.UpdateSource();
    }

    /// <summary>«Повторить с новой таблицей»: выбор файла сразу при открытии окна.</summary>
    public void OpenCsvOnLoad() => OpenCsv_Click(this, new RoutedEventArgs());

    void OpenCsv_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new OpenFileDialog { Filter = Loc.S("FemFieldCsvFilter") };
        if (dlg.ShowDialog(this) != true) return;
        Load(() => PointTable.ReadFile(dlg.FileName), Path.GetFileName(dlg.FileName));
    }

    void Paste_Click(object sender, RoutedEventArgs e)
    {
        if (!Clipboard.ContainsText()) return;
        Load(Clipboard.GetText, null);
    }

    /// <summary>Текст таблицы из файла или буфера; файл занят (открыт в Excel) или буфер недоступен — сообщение в окне.</summary>
    void Load(Func<string> read, string? fileName)
    {
        string text;
        try { text = read(); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Runtime.InteropServices.COMException)
        {
            ShowError(ex.Message);
            return;
        }
        errorText.Visibility = Visibility.Collapsed;
        _vm.LoadText(text, fileName);
        SyncFromVm();
    }

    void ShowError(string message)
    {
        errorText.Text = message;
        errorText.Visibility = Visibility.Visible;
    }

    void Coordinate_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (_syncing) return;
        _vm.XColumn = Math.Max(0, xBox.SelectedIndex);
        _vm.YColumn = Math.Max(0, yBox.SelectedIndex);
        _vm.ZColumn = zBox.SelectedIndex - 1;
        if (sender == xBox && _vm.Headers.Count > 0) _vm.LengthScale = PointTable.LengthScaleFromHeader(_vm.Headers[_vm.XColumn]);
        _vm.RebuildValueColumns();
        SyncFromVm();
    }

    void Quantity_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (_syncing || quantityBox.SelectedItem is not ComboBoxItem { Tag: string code } || code == _vm.Quantity) return;
        _vm.SetQuantity(code);
        SyncFromVm();
    }

    void LengthUnit_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (!_syncing && lengthUnitBox.SelectedItem is ComboBoxItem { Tag: string tag })
            _vm.LengthScale = double.Parse(tag, CultureInfo.InvariantCulture);
    }

    void Method_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (!_syncing && methodBox.SelectedItem is ComboBoxItem { Tag: FieldMethod m }) _vm.Method = m;
    }

    void FitExtents_Click(object sender, RoutedEventArgs e) { CommitEdits(); _vm.FitByExtents(); _vm.Preview(); RefreshPreview(); }
    void FitPoints_Click(object sender, RoutedEventArgs e) { CommitEdits(); _vm.FitByPoints(); _vm.Preview(); RefreshPreview(); }
    void Preview_Click(object sender, RoutedEventArgs e) { CommitEdits(); _vm.Preview(); RefreshPreview(); }
    void Patch_Changed(object sender, SelectionChangedEventArgs e) => previewCanvas.PatchIndex = Math.Max(0, patchBox.SelectedIndex);

    void Ok_Click(object sender, RoutedEventArgs e)
    {
        CommitEdits();
        if (_vm.Validate() is { } error)
        {
            ShowError(error);
            return;
        }
        DialogResult = true;
    }
}
