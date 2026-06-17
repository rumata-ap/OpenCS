using OpenCS.Utilites;
using CalcSettings = OpenCS.Utilites.CalcSettings;
using CsvExportSettings = OpenCS.Utilites.CsvExportSettings;

using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;

namespace OpenCS.Views
{
   public partial class SettingsWindow : Window
   {
      readonly AppViewModel _mvm;
      readonly PlotSettings _settings;
      readonly CalcSettings _calcSettings;
      readonly CsvExportSettings _csvSettings;

      static readonly string[] _palette =
      [
         "#FFFFFF","#F0F0F0","#D3D3D3","#A0A0A0","#606060","#333333","#000000",
         "#FF0000","#FF8000","#FFC300","#80FF00","#00C800","#00A0FF","#003A6C",
         "#9000FF","#FF00C0","#F0EACD","#FFFACD","#E0FFE0","#E0F0FF","#F0E0FF"
      ];

      public SettingsWindow(AppViewModel mvm)
      {
         InitializeComponent();
         _mvm = mvm;
         _settings = mvm.PlotSettings.Clone();
         _calcSettings = mvm.CalcSettings.Clone();
         _csvSettings = mvm.CsvSettings.Clone();
         Owner = Application.Current.MainWindow;

         LoadToUi();
         HookTextBoxes();
         BuildPalette();
         LoadCalcToUi();
         HookCalcBoxes();
         LoadCsvToUi();
         HookCsvControls();
      }

      void BuildPalette()
      {
         foreach (var hex in _palette)
         {
            var rect = new Rectangle
            {
               Width = 20, Height = 20, Margin = new Thickness(2),
               Stroke = Brushes.Gray, StrokeThickness = 0.5,
               RadiusX = 2, RadiusY = 2, Cursor = System.Windows.Input.Cursors.Hand,
               ToolTip = hex
            };
            try { rect.Fill = new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex)); }
            catch { rect.Fill = Brushes.Gray; }
            rect.MouseLeftButtonDown += (_, _) =>
            {
               if (Keyboard.FocusedElement is TextBox tb)
                  tb.Text = hex;
            };
            PalettePanel.Children.Add(rect);
         }
      }

      void LoadToUi()
      {
         BgBox.Text = _settings.Background;
         GridBox.Text = _settings.Grid;
         CurveBox.Text = _settings.Curve;
         FillBox.Text = _settings.Fill;
         MarkerBox.Text = _settings.MarkerFill;
         TextBoxField.Text = _settings.Text;
         HighlightBox.Text = _settings.Highlight;
         AxesColorBox.Text = _settings.AxesColor;
         AxesFontSzBox.Text = _settings.AxesFontSize.ToString("F0");
         DxfBgBox.Text = _settings.DxfCanvasBackground;
         CentroidColorBox.Text = _settings.CentroidColor;
         CentroidSzBox.Text = _settings.CentroidSize.ToString("F0");
         ScaleXBox.Text = _settings.ScaleX.ToString("F4");
         ScaleYBox.Text = _settings.ScaleY.ToString("F4");
         CurveThBox.Text = _settings.CurveThickness.ToString("F1");
         MarkerSzBox.Text = _settings.MarkerSize.ToString("F0");
         FontSzBox.Text = _settings.FontSize.ToString("F0");
         ShowGridCb.IsChecked = _settings.ShowGrid;
         GridThBox.Text = _settings.GridThickness.ToString("F2");
         TickCountBox.Text = _settings.TickCount.ToString();
         ShowLabelsCb.IsChecked = _settings.ShowPointLabels;
         ShowTooltipsCb.IsChecked = _settings.ShowTooltips;
         ShowAxesValsCb.IsChecked = _settings.ShowAxesValues;
         AxesOriginCb.IsChecked = _settings.AxesAtOrigin;
         UpdateSwatches();
      }

      void HookTextBoxes()
      {
         BgBox.TextChanged += (_, _) => { _settings.Background = BgBox.Text; UpdateSwatch(BgSwatch, BgBox.Text); };
         GridBox.TextChanged += (_, _) => { _settings.Grid = GridBox.Text; UpdateSwatch(GridSwatch, GridBox.Text); };
         CurveBox.TextChanged += (_, _) => { _settings.Curve = CurveBox.Text; UpdateSwatch(CurveSwatch, CurveBox.Text); };
         FillBox.TextChanged += (_, _) => { _settings.Fill = FillBox.Text; UpdateSwatch(FillSwatch, FillBox.Text); };
         MarkerBox.TextChanged += (_, _) => { _settings.MarkerFill = MarkerBox.Text; UpdateSwatch(MarkerSwatch, MarkerBox.Text); };
         TextBoxField.TextChanged += (_, _) => { _settings.Text = TextBoxField.Text; UpdateSwatch(TextSwatch, TextBoxField.Text); };
         HighlightBox.TextChanged += (_, _) => { _settings.Highlight = HighlightBox.Text; UpdateSwatch(HighlightSwatch, HighlightBox.Text); };
         AxesColorBox.TextChanged += (_, _) => { _settings.AxesColor = AxesColorBox.Text; UpdateSwatch(AxesColorSwatch, AxesColorBox.Text); };
         DxfBgBox.TextChanged += (_, _) => { _settings.DxfCanvasBackground = DxfBgBox.Text; UpdateSwatch(DxfBgSwatch, DxfBgBox.Text); };
         CentroidColorBox.TextChanged += (_, _) => { _settings.CentroidColor = CentroidColorBox.Text; UpdateSwatch(CentroidSwatch, CentroidColorBox.Text); };
         CentroidSzBox.TextChanged += (_, _) => { if (double.TryParse(CentroidSzBox.Text, out var v) && v > 0) _settings.CentroidSize = v; };
         AxesFontSzBox.TextChanged += (_, _) => { if (double.TryParse(AxesFontSzBox.Text, out var v) && v > 0) _settings.AxesFontSize = v; };
         ScaleXBox.TextChanged += (_, _) => { if (double.TryParse(ScaleXBox.Text, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var v)) _settings.ScaleX = v; };
         ScaleYBox.TextChanged += (_, _) => { if (double.TryParse(ScaleYBox.Text, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var v)) _settings.ScaleY = v; };
         CurveThBox.TextChanged += (_, _) => { if (double.TryParse(CurveThBox.Text, out var v) && v > 0) _settings.CurveThickness = v; };
         MarkerSzBox.TextChanged += (_, _) => { if (double.TryParse(MarkerSzBox.Text, out var v) && v > 0) _settings.MarkerSize = v; };
         FontSzBox.TextChanged += (_, _) => { if (double.TryParse(FontSzBox.Text, out var v) && v > 0) _settings.FontSize = v; };
         ShowGridCb.Checked += (_, _) => _settings.ShowGrid = true;
         ShowGridCb.Unchecked += (_, _) => _settings.ShowGrid = false;
         GridThBox.TextChanged += (_, _) => { if (double.TryParse(GridThBox.Text, out var v) && v > 0) _settings.GridThickness = v; };
         TickCountBox.TextChanged += (_, _) => { if (int.TryParse(TickCountBox.Text, out var v) && v > 0) _settings.TickCount = v; };
         ShowLabelsCb.Checked += (_, _) => _settings.ShowPointLabels = true;
         ShowLabelsCb.Unchecked += (_, _) => _settings.ShowPointLabels = false;
         ShowTooltipsCb.Checked += (_, _) => _settings.ShowTooltips = true;
         ShowTooltipsCb.Unchecked += (_, _) => _settings.ShowTooltips = false;
         ShowAxesValsCb.Checked += (_, _) => _settings.ShowAxesValues = true;
         ShowAxesValsCb.Unchecked += (_, _) => _settings.ShowAxesValues = false;
         AxesOriginCb.Checked += (_, _) => _settings.AxesAtOrigin = true;
         AxesOriginCb.Unchecked += (_, _) => _settings.AxesAtOrigin = false;
      }

      void UpdateSwatches()
      {
         UpdateSwatch(BgSwatch, BgBox.Text);
         UpdateSwatch(GridSwatch, GridBox.Text);
         UpdateSwatch(CurveSwatch, CurveBox.Text);
         UpdateSwatch(FillSwatch, FillBox.Text);
         UpdateSwatch(MarkerSwatch, MarkerBox.Text);
         UpdateSwatch(TextSwatch, TextBoxField.Text);
         UpdateSwatch(HighlightSwatch, HighlightBox.Text);
         UpdateSwatch(AxesColorSwatch, AxesColorBox.Text);
         UpdateSwatch(DxfBgSwatch, DxfBgBox.Text);
         UpdateSwatch(CentroidSwatch, CentroidColorBox.Text);
      }

      static void UpdateSwatch(Rectangle rect, string hex)
      {
         try { rect.Fill = new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex)); }
         catch { rect.Fill = Brushes.Gray; }
      }

      void Apply_Click(object sender, RoutedEventArgs e)
      {
         SaveAndApply();
      }

      void Ok_Click(object sender, RoutedEventArgs e)
      {
         SaveAndApply();
         Close();
      }

      void SaveAndApply()
      {
         _mvm.PlotSettings = _settings.Clone();
         _mvm.ApplyPlotSettings();
         _mvm.db.SavePlotSettings(_mvm.PlotSettings);

         _mvm.CalcSettings = _calcSettings.Clone();
         _mvm.db.SaveCalcSettings(_mvm.CalcSettings);

         _mvm.CsvSettings = _csvSettings.Clone();
         _mvm.db.SaveCsvSettings(_mvm.CsvSettings);
      }

      void Cancel_Click(object sender, RoutedEventArgs e)
      {
         Close();
      }

      void LoadCalcToUi()
      {
         GridDensityBox.Text   = _calcSettings.GridDensity.ToString();
         NewtonTolBox.Text     = _calcSettings.NewtonTolerance.ToString("G6", System.Globalization.CultureInfo.InvariantCulture);
         NewtonMaxIterBox.Text = _calcSettings.NewtonMaxIter.ToString();
         NewtonDeltaHBox.Text  = _calcSettings.NewtonDeltaH.ToString("G4", System.Globalization.CultureInfo.InvariantCulture);

         HullColorBox.Text         = _calcSettings.HullColor;
         HullThickBox.Text         = _calcSettings.HullThickness.ToString("F1");
         HoleColorBox.Text         = _calcSettings.HoleColor;
         HoleThickBox.Text         = _calcSettings.HoleThickness.ToString("F1");
         NeutralAxisColorBox.Text  = _calcSettings.NeutralAxisColor;
         NeutralAxisThickBox.Text  = _calcSettings.NeutralAxisThickness.ToString("F1");
         CentroidNdsColorBox.Text  = _calcSettings.CentroidNdsColor;
         CentroidNdsSizeBox.Text   = _calcSettings.CentroidNdsSize.ToString("F0");
         LabelFontSizeBox.Text     = _calcSettings.FiberLabelFontSize.ToString("F0");
         Sp63EtaMinBox.Text        = _calcSettings.Sp63DescEtaMin.ToString("G4", System.Globalization.CultureInfo.InvariantCulture);
         BatchParallelCb.IsChecked = _calcSettings.BatchParallel;
         UpdateCalcSwatches();
      }

      void HookCalcBoxes()
      {
         GridDensityBox.TextChanged += (_, _) =>
         {
            if (int.TryParse(GridDensityBox.Text, out var v) && v >= 1) _calcSettings.GridDensity = v;
         };
         NewtonTolBox.TextChanged += (_, _) =>
         {
            if (double.TryParse(NewtonTolBox.Text, System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture, out var v) && v > 0)
               _calcSettings.NewtonTolerance = v;
         };
         NewtonMaxIterBox.TextChanged += (_, _) =>
         {
            if (int.TryParse(NewtonMaxIterBox.Text, out var v) && v >= 1) _calcSettings.NewtonMaxIter = v;
         };
         NewtonDeltaHBox.TextChanged += (_, _) =>
         {
            if (double.TryParse(NewtonDeltaHBox.Text, System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture, out var v) && v > 0)
               _calcSettings.NewtonDeltaH = v;
         };

         HullColorBox.TextChanged += (_, _) =>
         {
            _calcSettings.HullColor = HullColorBox.Text;
            UpdateSwatch(HullSwatch, HullColorBox.Text);
         };
         HullThickBox.TextChanged += (_, _) =>
         {
            if (double.TryParse(HullThickBox.Text, out var v) && v > 0) _calcSettings.HullThickness = v;
         };
         HoleColorBox.TextChanged += (_, _) =>
         {
            _calcSettings.HoleColor = HoleColorBox.Text;
            UpdateSwatch(HoleSwatch, HoleColorBox.Text);
         };
         HoleThickBox.TextChanged += (_, _) =>
         {
            if (double.TryParse(HoleThickBox.Text, out var v) && v > 0) _calcSettings.HoleThickness = v;
         };
         NeutralAxisColorBox.TextChanged += (_, _) =>
         {
            _calcSettings.NeutralAxisColor = NeutralAxisColorBox.Text;
            UpdateSwatch(NeutralAxisSwatch, NeutralAxisColorBox.Text);
         };
         NeutralAxisThickBox.TextChanged += (_, _) =>
         {
            if (double.TryParse(NeutralAxisThickBox.Text, out var v) && v > 0) _calcSettings.NeutralAxisThickness = v;
         };
         CentroidNdsColorBox.TextChanged += (_, _) =>
         {
            _calcSettings.CentroidNdsColor = CentroidNdsColorBox.Text;
            UpdateSwatch(CentroidNdsSwatch, CentroidNdsColorBox.Text);
         };
         CentroidNdsSizeBox.TextChanged += (_, _) =>
         {
            if (double.TryParse(CentroidNdsSizeBox.Text, out var v) && v > 0) _calcSettings.CentroidNdsSize = v;
         };
         LabelFontSizeBox.TextChanged += (_, _) =>
         {
            if (double.TryParse(LabelFontSizeBox.Text, out var v) && v > 0) _calcSettings.FiberLabelFontSize = v;
         };
         Sp63EtaMinBox.TextChanged += (_, _) =>
         {
            if (double.TryParse(Sp63EtaMinBox.Text, System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture, out var v) && v > 0 && v < 1)
               _calcSettings.Sp63DescEtaMin = v;
         };
         BatchParallelCb.Checked   += (_, _) => _calcSettings.BatchParallel = true;
         BatchParallelCb.Unchecked += (_, _) => _calcSettings.BatchParallel = false;
      }

      void LoadCsvToUi()
      {
         CsvSemicolon.IsChecked = _csvSettings.Delimiter == ";";
         CsvComma.IsChecked     = _csvSettings.Delimiter == ",";
         CsvUtf8.IsChecked      = _csvSettings.Encoding == "utf-8";
         CsvWin1251.IsChecked   = _csvSettings.Encoding == "windows-1251";
      }

      void HookCsvControls()
      {
         CsvSemicolon.Checked += (_, _) => _csvSettings.Delimiter = ";";
         CsvComma.Checked     += (_, _) => _csvSettings.Delimiter = ",";
         CsvUtf8.Checked      += (_, _) => _csvSettings.Encoding = "utf-8";
         CsvWin1251.Checked   += (_, _) => _csvSettings.Encoding = "windows-1251";
      }

      void UpdateCalcSwatches()
      {
         UpdateSwatch(HullSwatch,        HullColorBox.Text);
         UpdateSwatch(HoleSwatch,        HoleColorBox.Text);
         UpdateSwatch(NeutralAxisSwatch, NeutralAxisColorBox.Text);
         UpdateSwatch(CentroidNdsSwatch, CentroidNdsColorBox.Text);
      }

      void Reset_Click(object sender, RoutedEventArgs e)
      {
         var def = PlotSettings.Default;
         _settings.Background = def.Background;
         _settings.Grid = def.Grid;
         _settings.Curve = def.Curve;
         _settings.Fill = def.Fill;
         _settings.MarkerFill = def.MarkerFill;
         _settings.Text = def.Text;
         _settings.Highlight = def.Highlight;
         _settings.CurveThickness = def.CurveThickness;
         _settings.MarkerSize = def.MarkerSize;
         _settings.FontSize = def.FontSize;
         _settings.ShowGrid = def.ShowGrid;
         _settings.ShowPointLabels = def.ShowPointLabels;
         _settings.ShowTooltips = def.ShowTooltips;
         _settings.ShowAxesValues = def.ShowAxesValues;
         _settings.AxesAtOrigin = def.AxesAtOrigin;
         _settings.AxesColor = def.AxesColor;
         _settings.AxesFontSize = def.AxesFontSize;
         _settings.GridThickness = def.GridThickness;
         _settings.TickCount = def.TickCount;
         _settings.ScaleX = def.ScaleX;
         _settings.ScaleY = def.ScaleY;
         _settings.DxfCanvasBackground = def.DxfCanvasBackground;
         _settings.CentroidColor = def.CentroidColor;
         _settings.CentroidSize = def.CentroidSize;
         LoadToUi();
      }
   }
}
