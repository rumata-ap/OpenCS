using OpenCS.Utilites;
using CalcSettings = OpenCS.Utilites.CalcSettings;
using CsvExportSettings = OpenCS.Utilites.CsvExportSettings;
using LiraImportSettings = OpenCS.Utilites.LiraImportSettings;
using AcadImportSettings = OpenCS.Utilites.AcadImportSettings;
using ArcDiscretization = OpenCS.Utilites.ArcDiscretization;

using System.IO;
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
      readonly LiraImportSettings _liraSettings;
       readonly AcadImportSettings _acadSettings;
       readonly GmshSettings _gmshSettings;

      public SettingsWindow(AppViewModel mvm)
      {
         InitializeComponent();
         _mvm = mvm;
         _settings = mvm.PlotSettings.Clone();
         _calcSettings = mvm.CalcSettings.Clone();
         _csvSettings = mvm.CsvSettings.Clone();
          _liraSettings = mvm.LiraImportSettings.Clone();
           _acadSettings = mvm.AcadImportSettings.Clone();
           _gmshSettings = mvm.GmshSettings.Clone();
          Owner = Application.Current.MainWindow;

         LoadToUi();
         HookTextBoxes();
         InitColorDefaults();
         LoadCalcToUi();
         HookCalcBoxes();
         LoadCsvToUi();
         HookCsvControls();
          LoadLiraToUi();
          HookLiraControls();
          LoadAcadToUi();
          HookAcadControls();
          LoadOpenSeesToUi();
           HookOpenSeesControls();
           LoadGmshToUi();
           HookGmshControls();
       }

      void LoadToUi()
      {
         BgField.Value = _settings.Background;
         GridField.Value = _settings.Grid;
         CurveField.Value = _settings.Curve;
         FillField.Value = _settings.Fill;
         MarkerField.Value = _settings.MarkerFill;
         TextField.Value = _settings.Text;
         HighlightField.Value = _settings.Highlight;
         AxesColorField.Value = _settings.AxesColor;
         AxesFontSzBox.Text = _settings.AxesFontSize.ToString("F0");
         DxfBgField.Value = _settings.DxfCanvasBackground;
         CentroidField.Value = _settings.CentroidColor;
         CentroidSzBox.Text = _settings.CentroidSize.ToString("F0");
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
         OriginReferenceAxesCb.IsChecked = _settings.ShowOriginReferenceAxes;
         ForceSetColorizeCb.IsChecked = _settings.ForceSetColorize;
         RefreshPreview();
      }

      /// <summary>Значения по умолчанию задают кнопки возврата в полях цвета.</summary>
      void InitColorDefaults()
      {
         var d = PlotSettings.Default;
         var c = CalcSettings.Default;
         BgField.DefaultValue = d.Background;
         GridField.DefaultValue = d.Grid;
         CurveField.DefaultValue = d.Curve;
         FillField.DefaultValue = d.Fill;
         MarkerField.DefaultValue = d.MarkerFill;
         TextField.DefaultValue = d.Text;
         HighlightField.DefaultValue = d.Highlight;
         AxesColorField.DefaultValue = d.AxesColor;
         DxfBgField.DefaultValue = d.DxfCanvasBackground;
         CentroidField.DefaultValue = d.CentroidColor;
         HullField.DefaultValue = c.HullColor;
         HoleField.DefaultValue = c.HoleColor;
         NeutralAxisField.DefaultValue = c.NeutralAxisColor;
         CentroidNdsField.DefaultValue = c.CentroidNdsColor;
      }

      void HookTextBoxes()
      {
         HookColor(BgField, v => _settings.Background = v);
         HookColor(GridField, v => _settings.Grid = v);
         HookColor(CurveField, v => _settings.Curve = v);
         HookColor(FillField, v => _settings.Fill = v);
         HookColor(MarkerField, v => _settings.MarkerFill = v);
         HookColor(TextField, v => _settings.Text = v);
         HookColor(HighlightField, v => _settings.Highlight = v);
         HookColor(AxesColorField, v => _settings.AxesColor = v);
         HookColor(DxfBgField, v => _settings.DxfCanvasBackground = v);
         HookColor(CentroidField, v => _settings.CentroidColor = v);

         HookNumber(CurveThBox, 0.1, 20, v => _settings.CurveThickness = v);
         HookNumber(GridThBox, 0.05, 5, v => _settings.GridThickness = v);
         HookNumber(MarkerSzBox, 1, 30, v => _settings.MarkerSize = v);
         HookNumber(FontSzBox, 5, 48, v => _settings.FontSize = v);
         HookNumber(AxesFontSzBox, 5, 48, v => _settings.AxesFontSize = v);
         HookNumber(CentroidSzBox, 1, 30, v => _settings.CentroidSize = v);
         HookNumber(TickCountBox, 2, 30, v => _settings.TickCount = (int)Math.Round(v), integer: true);

         HookCheck(ShowGridCb, v => _settings.ShowGrid = v);
         HookCheck(ShowLabelsCb, v => _settings.ShowPointLabels = v);
         HookCheck(ShowTooltipsCb, v => _settings.ShowTooltips = v);
         HookCheck(ShowAxesValsCb, v => _settings.ShowAxesValues = v);
         HookCheck(AxesOriginCb, v => _settings.AxesAtOrigin = v);
         HookCheck(OriginReferenceAxesCb, v => _settings.ShowOriginReferenceAxes = v);
         HookCheck(ForceSetColorizeCb, v => _settings.ForceSetColorize = v);
      }

      void HookColor(Helpers.ColorField f, Action<string> set) =>
         f.ValueChanged += v => { set(v); RefreshPreview(); };

      void HookCheck(CheckBox cb, Action<bool> set)
      {
         cb.Checked += (_, _) => { set(true); RefreshPreview(); };
         cb.Unchecked += (_, _) => { set(false); RefreshPreview(); };
      }

      /// <summary>
      /// Числовое поле с проверкой диапазона: при неверном вводе рамка краснеет и в подсказке
      /// написано, что допустимо; прежнее значение настройки при этом не меняется.
      /// </summary>
      void HookNumber(TextBox tb, double min, double max, Action<double> set, bool integer = false)
      {
         tb.TextChanged += (_, _) =>
         {
            double v = 0;
            bool ok = integer
               ? int.TryParse(tb.Text, out var iv) && (v = iv) >= min && v <= max
               : Pars.ParseAny(tb.Text, out v) && v >= min && v <= max;
            if (ok)
            {
               tb.ClearValue(Control.BorderBrushProperty);
               tb.ClearValue(FrameworkElement.ToolTipProperty);
               set(v);
               RefreshPreview();
            }
            else
            {
               tb.BorderBrush = Brushes.Crimson;
               tb.ToolTip = string.Format(Loc.S(integer ? "SettingsIntRangeTip" : "SettingsRangeTip"), min, max);
            }
         };
      }

      // ── Живой предпросмотр ───────────────────────────────────────────────

      Services.WpfPlotService? _preview;

      void RefreshPreview()
      {
         if (PreviewCanvas is null) return;
         _preview ??= new Services.WpfPlotService(PreviewCanvas);
         _preview.Clear();
         _preview.ApplySettings(_settings);

         // Условная диаграмма σ–ε бетона: заливка под кривой, кривая и маркеры.
         const int n = 40;
         var xs = new double[n + 2];
         var ys = new double[n + 2];
         for (int i = 0; i < n; i++)
         {
            double t = i / (double)(n - 1);
            xs[i] = 3.5 * t;
            ys[i] = 30 * (1 - (1 - t) * (1 - t));
         }
         xs[n] = xs[n - 1]; ys[n] = 0;
         xs[n + 1] = 0; ys[n + 1] = 0;
         _preview.AddPolygon(xs, ys, _settings.Fill, _settings.Curve);

         var mx = new[] { xs[0], xs[8], xs[16], xs[24], xs[32], xs[n - 1] };
         var my = new[] { ys[0], ys[8], ys[16], ys[24], ys[32], ys[n - 1] };
         _preview.AddMarkers(mx, my);

         _preview.SetXLabel(Loc.S("SettingsPreviewX"));
         _preview.SetYLabel(Loc.S("SettingsPreviewY"));
         _preview.Refresh();
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
         _mvm.NotifyCalcSettingsApplied();

         _mvm.CsvSettings = _csvSettings.Clone();
         _mvm.db.SaveCsvSettings(_mvm.CsvSettings);

          _mvm.LiraImportSettings = _liraSettings.Clone();
          _mvm.db.SaveLiraImportSettings(_mvm.LiraImportSettings);

           _mvm.AcadImportSettings = _acadSettings.Clone();
           _mvm.db.SaveAcadImportSettings(_mvm.AcadImportSettings);

           _mvm.GmshSettings = _gmshSettings.Clone();
           _mvm.db.SaveGmshSettings(_mvm.GmshSettings);
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
         LimitBisectAbsBox.Text = _calcSettings.LimitBisectAbsTol.ToString("G4", System.Globalization.CultureInfo.InvariantCulture);
         LimitBisectRelBox.Text = _calcSettings.LimitBisectRelTol.ToString("G4", System.Globalization.CultureInfo.InvariantCulture);

         HullField.Value           = _calcSettings.HullColor;
         HullThickBox.Text         = _calcSettings.HullThickness.ToString("F1");
         HoleField.Value           = _calcSettings.HoleColor;
         HoleThickBox.Text         = _calcSettings.HoleThickness.ToString("F1");
         NeutralAxisField.Value    = _calcSettings.NeutralAxisColor;
         NeutralAxisThickBox.Text  = _calcSettings.NeutralAxisThickness.ToString("F1");
         CentroidNdsField.Value    = _calcSettings.CentroidNdsColor;
         CentroidNdsSizeBox.Text   = _calcSettings.CentroidNdsSize.ToString("F0");
         LabelFontSizeBox.Text     = _calcSettings.FiberLabelFontSize.ToString("F0");
         Sp63EtaMinBox.Text        = _calcSettings.Sp63DescEtaMin.ToString("G4", System.Globalization.CultureInfo.InvariantCulture);
         EkbEtaMinBox.Text         = _calcSettings.EkbDescEtaMin.ToString("G4", System.Globalization.CultureInfo.InvariantCulture);
         NewtonJacobianCombo.SelectedIndex = _calcSettings.NewtonJacobian == "central" ? 1 : 0;
         ShellTolResBox.Text         = _calcSettings.ShellNewtonTolRes.ToString("G4", System.Globalization.CultureInfo.InvariantCulture);
         Sp20GammaGBox.Text          = _calcSettings.Sp20GammaFPermanent.ToString("G4", System.Globalization.CultureInfo.InvariantCulture);
         Sp20GammaGFavBox.Text       = _calcSettings.Sp20GammaFPermanentFav.ToString("G4", System.Globalization.CultureInfo.InvariantCulture);
         Sp20GammaLBox.Text          = _calcSettings.Sp20GammaFLongTerm.ToString("G4", System.Globalization.CultureInfo.InvariantCulture);
         Sp20GammaQBox.Text          = _calcSettings.Sp20GammaFShortTerm.ToString("G4", System.Globalization.CultureInfo.InvariantCulture);
         Sp20GammaABox.Text          = _calcSettings.Sp20GammaFAccidental.ToString("G4", System.Globalization.CultureInfo.InvariantCulture);
         Sp20LoadValueKindCombo.SelectedIndex = _calcSettings.Sp20LoadValueKind == "design" ? 1 : 0;
         BatchParallelCb.IsChecked   = _calcSettings.BatchParallel;
         ShellWarmStartCb.IsChecked  = _calcSettings.ShellWarmStart;
         RebarDifferentialDiagramCb.IsChecked = _calcSettings.RebarDifferentialDiagram;
         RebarCompressionByRsCb.IsChecked = _calcSettings.RebarCompressionByRs;
         ConsiderConcreteTensionUlsCb.IsChecked = _calcSettings.ConsiderConcreteTensionUls;
         ElasticStiffnessWithoutRebarCb.IsChecked = _calcSettings.ElasticStiffnessWithoutRebar;
         SelectComboByTag(FireCompressionZoneMethodCombo, _calcSettings.FireCompressionZoneMethod);
         PsiSMethodCombo.SelectedIndex = _calcSettings.CrackWidthPsiSMethod == "strain8232" ? 1 : 0;
         StageStrainModeCombo.SelectedIndex = _calcSettings.StageStrainMode == "fibers" ? 1 : 0;
         ShearStationStepBox.Text    = _calcSettings.ShearStationStep.ToString("G4", System.Globalization.CultureInfo.InvariantCulture);
         ShearProjectionStepBox.Text = _calcSettings.ShearProjectionStep.ToString("G4", System.Globalization.CultureInfo.InvariantCulture);
         ShearMomentZoneBox.Text     = _calcSettings.ShearMomentZoneLength.ToString("G4", System.Globalization.CultureInfo.InvariantCulture);
         ShearAnchorageFactorBox.Text = _calcSettings.ShearAnchorageFactor.ToString("G4", System.Globalization.CultureInfo.InvariantCulture);
      }

      void HookCalcBoxes()
      {
         GridDensityBox.TextChanged += (_, _) =>
         {
            if (int.TryParse(GridDensityBox.Text, out var v) && v >= 1) _calcSettings.GridDensity = v;
         };
         NewtonTolBox.TextChanged += (_, _) =>
         {
            if (Pars.ParseAny(NewtonTolBox.Text, out var v) && v > 0)
               _calcSettings.NewtonTolerance = v;
         };
         NewtonMaxIterBox.TextChanged += (_, _) =>
         {
            if (int.TryParse(NewtonMaxIterBox.Text, out var v) && v >= 1) _calcSettings.NewtonMaxIter = v;
         };
         NewtonDeltaHBox.TextChanged += (_, _) =>
         {
            if (Pars.ParseAny(NewtonDeltaHBox.Text, out var v) && v > 0)
               _calcSettings.NewtonDeltaH = v;
         };
         LimitBisectAbsBox.TextChanged += (_, _) =>
         {
            if (Pars.ParseAny(LimitBisectAbsBox.Text, out var v) && v > 0)
               _calcSettings.LimitBisectAbsTol = v;
         };
         LimitBisectRelBox.TextChanged += (_, _) =>
         {
            if (Pars.ParseAny(LimitBisectRelBox.Text, out var v) && v >= 0)
               _calcSettings.LimitBisectRelTol = v;
         };
         NewtonJacobianCombo.SelectionChanged += (_, _) =>
         {
            _calcSettings.NewtonJacobian =
               (NewtonJacobianCombo.SelectedItem as System.Windows.Controls.ComboBoxItem)?.Tag as string ?? "forward";
         };

         HookColor(HullField, v => _calcSettings.HullColor = v);
         HookColor(HoleField, v => _calcSettings.HoleColor = v);
         HookColor(NeutralAxisField, v => _calcSettings.NeutralAxisColor = v);
         HookColor(CentroidNdsField, v => _calcSettings.CentroidNdsColor = v);
         HookNumber(HullThickBox, 0.1, 20, v => _calcSettings.HullThickness = v);
         HookNumber(HoleThickBox, 0.1, 20, v => _calcSettings.HoleThickness = v);
         HookNumber(NeutralAxisThickBox, 0.1, 20, v => _calcSettings.NeutralAxisThickness = v);
         HookNumber(CentroidNdsSizeBox, 1, 30, v => _calcSettings.CentroidNdsSize = v);
         HookNumber(LabelFontSizeBox, 5, 48, v => _calcSettings.FiberLabelFontSize = v);
         Sp63EtaMinBox.TextChanged += (_, _) =>
         {
            if (Pars.ParseAny(Sp63EtaMinBox.Text, out var v) && v > 0 && v < 1)
               _calcSettings.Sp63DescEtaMin = v;
         };
         EkbEtaMinBox.TextChanged += (_, _) =>
         {
            if (Pars.ParseAny(EkbEtaMinBox.Text, out var v) && v > 0 && v < 1)
               _calcSettings.EkbDescEtaMin = v;
         };
         ShellTolResBox.TextChanged += (_, _) =>
         {
            if (Pars.ParseAny(ShellTolResBox.Text, out var v) && v > 0)
               _calcSettings.ShellNewtonTolRes = v;
         };
         HookSp20GammaBox(Sp20GammaGBox, v => _calcSettings.Sp20GammaFPermanent = v);
         HookSp20GammaBox(Sp20GammaGFavBox, v => _calcSettings.Sp20GammaFPermanentFav = v);
         HookSp20GammaBox(Sp20GammaLBox, v => _calcSettings.Sp20GammaFLongTerm = v);
         HookSp20GammaBox(Sp20GammaQBox, v => _calcSettings.Sp20GammaFShortTerm = v);
         HookSp20GammaBox(Sp20GammaABox, v => _calcSettings.Sp20GammaFAccidental = v);
         Sp20LoadValueKindCombo.SelectionChanged += (_, _) =>
            _calcSettings.Sp20LoadValueKind = ComboTag(Sp20LoadValueKindCombo) ?? "normative";
         BatchParallelCb.Checked    += (_, _) => _calcSettings.BatchParallel  = true;
         BatchParallelCb.Unchecked  += (_, _) => _calcSettings.BatchParallel  = false;
         ShellWarmStartCb.Checked   += (_, _) => _calcSettings.ShellWarmStart = true;
         ShellWarmStartCb.Unchecked += (_, _) => _calcSettings.ShellWarmStart = false;
         RebarDifferentialDiagramCb.Checked   += (_, _) => _calcSettings.RebarDifferentialDiagram = true;
         RebarDifferentialDiagramCb.Unchecked += (_, _) => _calcSettings.RebarDifferentialDiagram = false;
         RebarCompressionByRsCb.Checked   += (_, _) => _calcSettings.RebarCompressionByRs = true;
         RebarCompressionByRsCb.Unchecked += (_, _) => _calcSettings.RebarCompressionByRs = false;
         ConsiderConcreteTensionUlsCb.Checked   += (_, _) => _calcSettings.ConsiderConcreteTensionUls = true;
         ConsiderConcreteTensionUlsCb.Unchecked += (_, _) => _calcSettings.ConsiderConcreteTensionUls = false;
         ElasticStiffnessWithoutRebarCb.Checked   += (_, _) => _calcSettings.ElasticStiffnessWithoutRebar = true;
         ElasticStiffnessWithoutRebarCb.Unchecked += (_, _) => _calcSettings.ElasticStiffnessWithoutRebar = false;
         FireCompressionZoneMethodCombo.SelectionChanged += (_, _) =>
         {
            _calcSettings.FireCompressionZoneMethod =
               ComboTag(FireCompressionZoneMethodCombo) ?? "auto";
         };
         StageStrainModeCombo.SelectionChanged += (_, _) =>
            _calcSettings.StageStrainMode = StageStrainModeCombo.SelectedIndex == 1 ? "fibers" : "plane";
         PsiSMethodCombo.SelectionChanged += (_, _) =>
         {
            _calcSettings.CrackWidthPsiSMethod =
               (PsiSMethodCombo.SelectedItem as System.Windows.Controls.ComboBoxItem)?.Tag as string ?? "stress8138";
         };
         // Шаги перебора и длина зоны допускают 0 («авто» / «2·h0» по норме), k — строго больше нуля.
         ShearStationStepBox.TextChanged += (_, _) =>
         {
            if (Pars.ParseAny(ShearStationStepBox.Text, out var v) && v >= 0)
               _calcSettings.ShearStationStep = v;
         };
         ShearProjectionStepBox.TextChanged += (_, _) =>
         {
            if (Pars.ParseAny(ShearProjectionStepBox.Text, out var v) && v >= 0)
               _calcSettings.ShearProjectionStep = v;
         };
         ShearMomentZoneBox.TextChanged += (_, _) =>
         {
            if (Pars.ParseAny(ShearMomentZoneBox.Text, out var v) && v >= 0)
               _calcSettings.ShearMomentZoneLength = v;
         };
         ShearAnchorageFactorBox.TextChanged += (_, _) =>
         {
            if (Pars.ParseAny(ShearAnchorageFactorBox.Text, out var v) && v > 0)
               _calcSettings.ShearAnchorageFactor = v;
         };
      }

      static void HookSp20GammaBox(System.Windows.Controls.TextBox box, Action<double> setter)
      {
         box.TextChanged += (_, _) =>
         {
            if (Pars.ParseAny(box.Text, out var v) && v > 0)
               setter(v);
         };
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

      void LoadLiraToUi()
      {
         const double ten = 10.0;
         LiraTon981.IsChecked = Math.Abs(_liraSettings.TonToKnFactor - ten) > 0.01;
         LiraTon10.IsChecked  = Math.Abs(_liraSettings.TonToKnFactor - ten) <= 0.01;
         LiraInvertBarMxMyCb.IsChecked   = _liraSettings.InvertBarBendingMoments;
         LiraInvertShellMxMyCb.IsChecked = _liraSettings.InvertShellBendingMoments;
         LiraTextDirBox.Text = _liraSettings.TextFileDirectory ?? "";
      }

      void HookLiraControls()
      {
         LiraTon981.Checked += (_, _) => _liraSettings.TonToKnFactor = 9.80665;
         LiraTon10.Checked  += (_, _) => _liraSettings.TonToKnFactor = 10.0;
         LiraInvertBarMxMyCb.Checked   += (_, _) => _liraSettings.InvertBarBendingMoments = true;
         LiraInvertBarMxMyCb.Unchecked += (_, _) => _liraSettings.InvertBarBendingMoments = false;
         LiraInvertShellMxMyCb.Checked   += (_, _) => _liraSettings.InvertShellBendingMoments = true;
         LiraInvertShellMxMyCb.Unchecked += (_, _) => _liraSettings.InvertShellBendingMoments = false;
         LiraTextDirBox.TextChanged += (_, _) =>
            _liraSettings.TextFileDirectory = string.IsNullOrWhiteSpace(LiraTextDirBox.Text) ? null : LiraTextDirBox.Text.Trim();
      }

      void LoadAcadToUi()
      {
         const double mm = 0.001;
         const double cm = 0.01;
         AcadMm.IsChecked = Math.Abs(_acadSettings.ScaleFactor - mm) < 0.0001;
         AcadCm.IsChecked = Math.Abs(_acadSettings.ScaleFactor - cm) < 0.0001;
         AcadM.IsChecked  = Math.Abs(_acadSettings.ScaleFactor - 1.0) < 0.0001;

         AcadArcChordRb.IsChecked = _acadSettings.ArcDiscretizationMode == ArcDiscretization.ChordLength;
         AcadArcFixedRb.IsChecked = _acadSettings.ArcDiscretizationMode == ArcDiscretization.FixedSegments;
         AcadArcChordBox.Text = _acadSettings.ArcChordLength.ToString("F4", System.Globalization.CultureInfo.InvariantCulture);
         AcadArcSegBox.Text = _acadSettings.ArcSegments.ToString();
      }

      void HookAcadControls()
      {
         AcadMm.Checked += (_, _) => _acadSettings.ScaleFactor = 0.001;
         AcadCm.Checked += (_, _) => _acadSettings.ScaleFactor = 0.01;
         AcadM.Checked  += (_, _) => _acadSettings.ScaleFactor = 1.0;

         AcadArcChordRb.Checked += (_, _) => _acadSettings.ArcDiscretizationMode = ArcDiscretization.ChordLength;
         AcadArcFixedRb.Checked += (_, _) => _acadSettings.ArcDiscretizationMode = ArcDiscretization.FixedSegments;
         AcadArcChordBox.TextChanged += (_, _) =>
         {
            if (Pars.ParseAny(AcadArcChordBox.Text, out var v) && v > 0)
               _acadSettings.ArcChordLength = v;
         };
         AcadArcSegBox.TextChanged += (_, _) =>
         {
            if (int.TryParse(AcadArcSegBox.Text, out var v) && v >= 3)
               _acadSettings.ArcSegments = v;
         };
      }

      void LoadOpenSeesToUi()
      {
         if (!double.IsFinite(_calcSettings.OpenSeesDefaultGjKnm2) || _calcSettings.OpenSeesDefaultGjKnm2 <= 0)
            _calcSettings.OpenSeesDefaultGjKnm2 = CalcSettings.DefaultOpenSeesGjKnm2;
         OpenSeesDefaultGjBox.Text = _calcSettings.OpenSeesDefaultGjKnm2.ToString("G6", System.Globalization.CultureInfo.InvariantCulture);
         OpenSeesAutoGjFromSectionCb.IsChecked = _calcSettings.OpenSeesAutoGjFromSection;
         SubmodelLinearPrecheckCb.IsChecked = _calcSettings.SubmodelLinearPrecheck;
         OpenSeesExeBox.Text = _calcSettings.OpenSeesExecutablePath ?? "";
         OpenSeesTimeoutBox.Text = _calcSettings.OpenSeesTimeoutSeconds.ToString();
         OpenSeesArtifactsPathBox.Text = _calcSettings.OpenSeesArtifactsPath ?? "";
         OpenSeesRefinementDivisionsBox.Text = _calcSettings.OpenSeesRefinementDivisions.ToString();
         OpenSeesMaxRefinementDepthBox.Text = _calcSettings.OpenSeesMaxRefinementDepth.ToString();
         OpenSeesToleranceBox.Text = _calcSettings.OpenSeesTolerance.ToString("G4", System.Globalization.CultureInfo.InvariantCulture);
         OpenSeesMaxIterationsBox.Text = _calcSettings.OpenSeesMaxIterations.ToString();
         SelectComboByTag(OpenSeesGeomTransfCombo, _calcSettings.OpenSeesGeomTransfKind);
         SelectComboByTag(OpenSeesConvergenceTestCombo, _calcSettings.OpenSeesConvergenceTest);
         OpenSeesIntegrationPointsBox.Text = _calcSettings.OpenSeesIntegrationPoints.ToString();
         SelectComboByTag(OpenSeesAlgorithmCombo, _calcSettings.OpenSeesAlgorithm);
         OpenSeesRecordFiberStatesCb.IsChecked = _calcSettings.OpenSeesRecordFiberStates;
         OpenSeesFiberStatesIntegrationPointsBox.Text = _calcSettings.OpenSeesFiberStatesIntegrationPoints ?? "";
         OpenSeesFiberStatesIntegrationPointsBox.IsEnabled = _calcSettings.OpenSeesRecordFiberStates;
         CsfeaThreadsBox.Text = Math.Max(0, _calcSettings.CsfeaThreads).ToString();
         CsfeaBarStiffnessAggregateCombo.SelectedIndex = _calcSettings.CsfeaBarStiffnessMean ? 1 : 0;
         CsfeaStiffnessFloorBox.Text = _calcSettings.ResolveCsfeaStiffnessFloor().ToString("G4", System.Globalization.CultureInfo.CurrentCulture);
      }

      void HookOpenSeesControls()
      {
         OpenSeesDefaultGjBox.TextChanged += (_, _) =>
         {
            if (Pars.ParseAny(OpenSeesDefaultGjBox.Text, out var v) && double.IsFinite(v) && v > 0)
               _calcSettings.OpenSeesDefaultGjKnm2 = v;
         };
         OpenSeesDefaultGjBox.LostFocus += (_, _) =>
         {
            if (Pars.ParseAny(OpenSeesDefaultGjBox.Text, out var v) && double.IsFinite(v) && v > 0) return;
            MessageBox.Show(Loc.S("OpenSeesDefaultGjInvalid"), Loc.S("OpenSeesSettingsTab"),
               MessageBoxButton.OK, MessageBoxImage.Warning);
            OpenSeesDefaultGjBox.Text = _calcSettings.OpenSeesDefaultGjKnm2.ToString("G6", System.Globalization.CultureInfo.InvariantCulture);
         };
         OpenSeesAutoGjFromSectionCb.Checked += (_, _) => _calcSettings.OpenSeesAutoGjFromSection = true;
         OpenSeesAutoGjFromSectionCb.Unchecked += (_, _) => _calcSettings.OpenSeesAutoGjFromSection = false;
         SubmodelLinearPrecheckCb.Checked += (_, _) => _calcSettings.SubmodelLinearPrecheck = true;
         SubmodelLinearPrecheckCb.Unchecked += (_, _) => _calcSettings.SubmodelLinearPrecheck = false;
         OpenSeesExeBox.TextChanged += (_, _) =>
            _calcSettings.OpenSeesExecutablePath = string.IsNullOrWhiteSpace(OpenSeesExeBox.Text) ? null : OpenSeesExeBox.Text.Trim();
         OpenSeesTimeoutBox.TextChanged += (_, _) =>
         {
            if (int.TryParse(OpenSeesTimeoutBox.Text, out var v) && v > 0) _calcSettings.OpenSeesTimeoutSeconds = v;
         };
         OpenSeesArtifactsPathBox.TextChanged += (_, _) =>
            _calcSettings.OpenSeesArtifactsPath = string.IsNullOrWhiteSpace(OpenSeesArtifactsPathBox.Text) ? null : OpenSeesArtifactsPathBox.Text.Trim();
         OpenSeesArtifactsPathBox.LostFocus += (_, _) =>
         {
            if (!OpenCS.OpenSees.Runtime.WindowsShortPath.IsAsciiSafe(OpenSeesArtifactsPathBox.Text))
               MessageBox.Show(Loc.S("OpenSeesArtifactsPathNonAsciiWarning"), Loc.S("OpenSeesSettingsTab"),
                  MessageBoxButton.OK, MessageBoxImage.Warning);
         };
         OpenSeesRefinementDivisionsBox.TextChanged += (_, _) =>
         {
            if (int.TryParse(OpenSeesRefinementDivisionsBox.Text, out var v) && v > 0) _calcSettings.OpenSeesRefinementDivisions = v;
         };
         OpenSeesMaxRefinementDepthBox.TextChanged += (_, _) =>
         {
            if (int.TryParse(OpenSeesMaxRefinementDepthBox.Text, out var v) && v > 0) _calcSettings.OpenSeesMaxRefinementDepth = v;
         };
         OpenSeesToleranceBox.TextChanged += (_, _) =>
         {
            if (Pars.ParseAny(OpenSeesToleranceBox.Text, out var v) && v > 0)
               _calcSettings.OpenSeesTolerance = v;
         };
         OpenSeesMaxIterationsBox.TextChanged += (_, _) =>
         {
            if (int.TryParse(OpenSeesMaxIterationsBox.Text, out var v) && v > 0) _calcSettings.OpenSeesMaxIterations = v;
         };
         OpenSeesGeomTransfCombo.SelectionChanged += (_, _) =>
            _calcSettings.OpenSeesGeomTransfKind = ComboTag(OpenSeesGeomTransfCombo) ?? "Linear";
         OpenSeesConvergenceTestCombo.SelectionChanged += (_, _) =>
            _calcSettings.OpenSeesConvergenceTest = ComboTag(OpenSeesConvergenceTestCombo) ?? "EnergyIncr";
         OpenSeesIntegrationPointsBox.TextChanged += (_, _) =>
         {
            if (int.TryParse(OpenSeesIntegrationPointsBox.Text, out var v) && v > 0) _calcSettings.OpenSeesIntegrationPoints = v;
         };
         OpenSeesAlgorithmCombo.SelectionChanged += (_, _) =>
            _calcSettings.OpenSeesAlgorithm = ComboTag(OpenSeesAlgorithmCombo) ?? "NewtonLineSearch";
         OpenSeesRecordFiberStatesCb.Checked += (_, _) =>
         {
            _calcSettings.OpenSeesRecordFiberStates = true;
            OpenSeesFiberStatesIntegrationPointsBox.IsEnabled = true;
         };
         OpenSeesRecordFiberStatesCb.Unchecked += (_, _) =>
         {
            _calcSettings.OpenSeesRecordFiberStates = false;
            OpenSeesFiberStatesIntegrationPointsBox.IsEnabled = false;
         };
         OpenSeesFiberStatesIntegrationPointsBox.TextChanged += (_, _) =>
            _calcSettings.OpenSeesFiberStatesIntegrationPoints =
               string.IsNullOrWhiteSpace(OpenSeesFiberStatesIntegrationPointsBox.Text) ? null : OpenSeesFiberStatesIntegrationPointsBox.Text.Trim();
         CsfeaThreadsBox.TextChanged += (_, _) =>
         {
            if (int.TryParse(CsfeaThreadsBox.Text, out var v) && v >= 0) _calcSettings.CsfeaThreads = v;
         };
         CsfeaBarStiffnessAggregateCombo.SelectionChanged += (_, _) =>
            _calcSettings.CsfeaBarStiffnessMean = CsfeaBarStiffnessAggregateCombo.SelectedIndex == 1;
         CsfeaStiffnessFloorBox.TextChanged += (_, _) =>
         {
            if (Pars.ParseAny(CsfeaStiffnessFloorBox.Text, out var v) && v is > 0 and < 1)
               _calcSettings.CsfeaStiffnessFloor = v;
         };
      }

      void LiraTextDirBrowse_Click(object sender, RoutedEventArgs e)
      {
         var initial = string.IsNullOrWhiteSpace(LiraTextDirBox.Text) ? null : LiraTextDirBox.Text.Trim();
         var folder = _mvm.FileDialogService.SelectFolder(Loc.S("LiraTextDirGroup"), initial);
         if (folder is not null) LiraTextDirBox.Text = folder;
      }

      void OpenSeesExeBrowse_Click(object sender, RoutedEventArgs e)
      {
         var path = _mvm.FileDialogService.OpenFile(Loc.S("OpenSeesExeFilter"), Loc.S("FemAnalysisExeLabel"));
         if (path is not null) OpenSeesExeBox.Text = path;
      }

       void OpenSeesArtifactsPathBrowse_Click(object sender, RoutedEventArgs e)
      {
         var initial = string.IsNullOrWhiteSpace(OpenSeesArtifactsPathBox.Text) ? null : OpenSeesArtifactsPathBox.Text.Trim();
         var folder = _mvm.FileDialogService.SelectFolder(Loc.S("OpenSeesArtifactsPathLabel"), initial);
         if (folder is not null) OpenSeesArtifactsPathBox.Text = folder;
       }

       void LoadGmshToUi()
       {
          GmshExeBox.Text = _gmshSettings.ExecutablePath ?? "";
          SelectComboByTag(GmshAlgorithmCombo, _gmshSettings.Algorithm.ToString());
          SelectComboByTag(GmshElementModeCombo, _gmshSettings.ElementMode.ToString());
          GmshTimeoutBox.Text = _gmshSettings.TimeoutSeconds.ToString();
          GmshSeamToleranceBox.Text = SeamToleranceMmText(_gmshSettings.SeamToleranceM);
          GmshKeepArtifactsCb.IsChecked = _gmshSettings.KeepArtifacts;
          GmshArtifactsPathBox.Text = _gmshSettings.ArtifactsPath ?? "";
       }

       /// <summary>Пресет «регулярная сетка»: Frontal-Delaunay for Quads с рекомбинацией — почти прямоугольные Q4,
       /// T3 только у отверстий, колонн и стыков (проба 08.10: медиана отклонения угла Q4 от 90° — 2° против 18° у
       /// Frontal-Delaunay, T3 в 8 раз меньше).</summary>
       void GmshPresetRegular_Click(object sender, RoutedEventArgs e)
       {
          SelectComboByTag(GmshAlgorithmCombo, "8");
          SelectComboByTag(GmshElementModeCombo, nameof(CScore.Planar.PlanarMeshElementMode.Mixed));
       }

       /// <summary>Пресет «свободная сетка» — прежние настройки по умолчанию (Frontal-Delaunay, T3+Q4).</summary>
       void GmshPresetFree_Click(object sender, RoutedEventArgs e)
       {
          SelectComboByTag(GmshAlgorithmCombo, "6");
          SelectComboByTag(GmshElementModeCombo, nameof(CScore.Planar.PlanarMeshElementMode.Mixed));
       }

       /// <summary>Допуск шва в миллиметрах для поля ввода.</summary>
       static string SeamToleranceMmText(double toleranceM) =>
          (toleranceM * 1000).ToString("G6", System.Globalization.CultureInfo.CurrentCulture);

       /// <summary>Разбирает допуск шва, введённый в мм; true — число в диапазоне 0,001–1 мм (результат в м).</summary>
       static bool TrySeamToleranceMm(string text, out double toleranceM)
       {
          toleranceM = 0;
          if (!Pars.ParseAny(text, out var mm) || !double.IsFinite(mm)) return false;
          toleranceM = mm / 1000;
          return toleranceM >= CScore.Fem.FemPlanarMeshPlan.MinSeamToleranceM && toleranceM <= CScore.Fem.FemPlanarMeshPlan.MaxSeamToleranceM;
       }

       void HookGmshControls()
       {
          GmshExeBox.TextChanged += (_, _) =>
             _gmshSettings.ExecutablePath = string.IsNullOrWhiteSpace(GmshExeBox.Text) ? null : GmshExeBox.Text.Trim();
          GmshAlgorithmCombo.SelectionChanged += (_, _) =>
             _gmshSettings.Algorithm = int.TryParse(ComboTag(GmshAlgorithmCombo), out var alg) ? alg : 6;
          GmshElementModeCombo.SelectionChanged += (_, _) =>
             _gmshSettings.ElementMode = Enum.TryParse<CScore.Planar.PlanarMeshElementMode>(ComboTag(GmshElementModeCombo), out var mode)
                ? mode : CScore.Planar.PlanarMeshElementMode.Mixed;
          GmshTimeoutBox.TextChanged += (_, _) =>
          {
             if (int.TryParse(GmshTimeoutBox.Text, out var v) && v > 0) _gmshSettings.TimeoutSeconds = v;
          };
          GmshSeamToleranceBox.TextChanged += (_, _) =>
          {
             if (TrySeamToleranceMm(GmshSeamToleranceBox.Text, out var m)) _gmshSettings.SeamToleranceM = m;
          };
          GmshSeamToleranceBox.LostFocus += (_, _) =>
          {
             if (TrySeamToleranceMm(GmshSeamToleranceBox.Text, out _)) return;
             MessageBox.Show(Loc.S("GmshSeamToleranceInvalid"), Loc.S("GmshSettingsTab"), MessageBoxButton.OK, MessageBoxImage.Warning);
             GmshSeamToleranceBox.Text = SeamToleranceMmText(_gmshSettings.SeamToleranceM);
          };
          GmshKeepArtifactsCb.Checked   += (_, _) => _gmshSettings.KeepArtifacts = true;
          GmshKeepArtifactsCb.Unchecked += (_, _) => _gmshSettings.KeepArtifacts = false;
          GmshArtifactsPathBox.TextChanged += (_, _) =>
             _gmshSettings.ArtifactsPath = string.IsNullOrWhiteSpace(GmshArtifactsPathBox.Text) ? null : GmshArtifactsPathBox.Text.Trim();
       }

       void GmshExeBrowse_Click(object sender, RoutedEventArgs e)
       {
          var path = _mvm.FileDialogService.OpenFile(Loc.S("GmshExeFilter"), Loc.S("GmshExecutablePath"));
          if (path is not null) GmshExeBox.Text = path;
       }

       void GmshArtifactsPathBrowse_Click(object sender, RoutedEventArgs e)
       {
          var initial = string.IsNullOrWhiteSpace(GmshArtifactsPathBox.Text) ? null : GmshArtifactsPathBox.Text.Trim();
          var folder = _mvm.FileDialogService.SelectFolder(Loc.S("GmshArtifactsPathLabel"), initial);
          if (folder is not null) GmshArtifactsPathBox.Text = folder;
       }

       async void GmshCheckVersion_Click(object sender, RoutedEventArgs e)
       {
          GmshCheckVersionBtn.IsEnabled = false;
          try
          {
             var explicitPath = string.IsNullOrWhiteSpace(GmshExeBox.Text) ? null : GmshExeBox.Text.Trim();
             var executable = new OpenCS.Gmsh.Runtime.GmshExecutableResolver().Resolve(explicitPath);
             var version = await OpenCS.Gmsh.Runtime.GmshProcessRunner.ReadVersionAsync(
                executable.Path, TimeSpan.FromSeconds(10), CancellationToken.None);
             GmshVersionText.Text = string.Format(Loc.S("GmshVersionResolvedFormat"), executable.Source, version);
          }
          catch (Exception ex) when (ex is FileNotFoundException or IOException or InvalidDataException)
          {
             GmshVersionText.Text = string.Format(Loc.S("GmshVersionNotFoundFormat"), ex.Message);
          }
          finally
          {
             GmshCheckVersionBtn.IsEnabled = true;
          }
       }

      static string? ComboTag(ComboBox combo) => (combo.SelectedItem as ComboBoxItem)?.Tag as string;

      static void SelectComboByTag(ComboBox combo, string tag)
      {
         foreach (var item in combo.Items.OfType<ComboBoxItem>())
            if (item.Tag as string == tag) { combo.SelectedItem = item; return; }
         if (combo.Items.Count > 0) combo.SelectedIndex = 0;
      }

      /// <summary>Сброс только вкладки «Графики»: графики, холст сечения и стили линий НДС.</summary>
      void ResetPlotTab_Click(object sender, RoutedEventArgs e)
      {
         var def = PlotSettings.Default;
         foreach (var pr in typeof(PlotSettings).GetProperties())
            if (pr.CanWrite) pr.SetValue(_settings, pr.GetValue(def));

         var cd = CalcSettings.Default;
         _calcSettings.HullColor = cd.HullColor;
         _calcSettings.HullThickness = cd.HullThickness;
         _calcSettings.HoleColor = cd.HoleColor;
         _calcSettings.HoleThickness = cd.HoleThickness;
         _calcSettings.NeutralAxisColor = cd.NeutralAxisColor;
         _calcSettings.NeutralAxisThickness = cd.NeutralAxisThickness;
         _calcSettings.CentroidNdsColor = cd.CentroidNdsColor;
         _calcSettings.CentroidNdsSize = cd.CentroidNdsSize;
         _calcSettings.FiberLabelFontSize = cd.FiberLabelFontSize;

         LoadToUi();
         LoadCalcToUi();
      }
   }
}
