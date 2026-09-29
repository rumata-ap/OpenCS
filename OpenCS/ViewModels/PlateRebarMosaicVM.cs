using System.Globalization;
using System.IO;
using System.Windows.Media;
using CScore.Import;
using CScore.PlateRebar;
using OpenCS.Utilites;

namespace OpenCS.ViewModels;

/// <summary>Что показывает мозаика армирования.</summary>
public enum PlateRebarMosaicSourceKind { None, Selected, Assigned, Difference }

/// <summary>Пункт списка источников мозаики.</summary>
public sealed record PlateRebarMosaicSourceOption(PlateRebarMosaicSourceKind Kind, string Label);

/// <summary>Пункт списка компонент мозаики.</summary>
public sealed record PlateRebarMosaicComponentOption(PlateRebarMosaicComponent Component, string Label);

/// <summary>Строка легенды мозаики: цвет, диапазон и число КЭ.</summary>
public sealed record PlateRebarMosaicLegendItem(Brush Brush, string Label, int Count);

/// <summary>Раскраска КЭ: цвет по тегу КЭ; КЭ без данных в словарь не попадают.</summary>
public sealed record PlateRebarMosaicColoring(IReadOnlyDictionary<string, Color> ColorByTag);

/// <summary>
/// Настройки и легенда мозаики армирования пластин (общие для 3D-вида схемы и редактора пластинчатого
/// КонЭ): источник, компонента, пороги шкалы. Источники армирования читаются из файлов при схеме
/// (сейчас — ASP и RBT ЛИРЫ).
/// </summary>
public sealed class PlateRebarMosaicVM : ViewModelBase
{
   /// <summary>Цвет КЭ, где программа-источник не смогла подобрать арматуру.</summary>
   public static Color FailureColor => Colors.Black;
   /// <summary>Цвет полосы «0».</summary>
   public static Color ZeroColor => Color.FromRgb(250, 250, 250);

   static readonly Color[] SequentialStops =
   [
      Color.FromRgb(49, 54, 149), Color.FromRgb(69, 117, 180), Color.FromRgb(116, 173, 209),
      Color.FromRgb(171, 217, 233), Color.FromRgb(254, 224, 144), Color.FromRgb(253, 174, 97),
      Color.FromRgb(244, 109, 67), Color.FromRgb(215, 48, 39), Color.FromRgb(165, 0, 38),
   ];
   static readonly Color[] DeficitStops = [Color.FromRgb(165, 0, 38), Color.FromRgb(252, 187, 161)];
   static readonly Color[] ReserveStops = [Color.FromRgb(199, 233, 192), Color.FromRgb(0, 109, 44)];

   readonly Action<string>? _warn;
   IPlateRebarFieldSource? _selected, _assigned;
   string? _selectedFile, _assignedFile;

   /// <summary>Настройки изменились — раскраску нужно пересчитать.</summary>
   public event EventHandler? Changed;

   /// <param name="warn">Куда сообщать об ошибках чтения файлов.</param>
   public PlateRebarMosaicVM(Action<string>? warn = null) => _warn = warn;

   public IReadOnlyList<PlateRebarMosaicSourceOption> SourceOptions { get; private set; } = [];
   public IReadOnlyList<PlateRebarMosaicComponentOption> ComponentOptions { get; private set; } = [];
   public IReadOnlyList<PlateRebarMosaicLegendItem> Legend { get; private set; } = [];

   /// <summary>Есть хотя бы один файл армирования.</summary>
   public bool HasData => _selected != null || _assigned != null;

   PlateRebarMosaicSourceOption? _selectedSource;
   public PlateRebarMosaicSourceOption? SelectedSource
   {
      get => _selectedSource;
      set
      {
         if (Equals(_selectedSource, value)) return;
         _selectedSource = value;
         OnPropertyChanged();
         OnPropertyChanged(nameof(IsActive));
         RefreshComponents();
         RaiseChanged();
      }
   }

   PlateRebarMosaicComponentOption? _selectedComponent;
   public PlateRebarMosaicComponentOption? SelectedComponent
   {
      get => _selectedComponent;
      set
      {
         if (Equals(_selectedComponent, value)) return;
         _selectedComponent = value;
         OnPropertyChanged();
         RaiseChanged();
      }
   }

   string _thresholdsText = "";
   /// <summary>Пороги шкалы через «;»; пусто — автошкала.</summary>
   public string ThresholdsText
   {
      get => _thresholdsText;
      set
      {
         value ??= "";
         if (_thresholdsText == value) return;
         _thresholdsText = value;
         OnPropertyChanged();
         RaiseChanged();
      }
   }

   string _legendTitle = "";
   public string LegendTitle { get => _legendTitle; private set { _legendTitle = value; OnPropertyChanged(); } }

   /// <summary>Мозаика включена и есть что показывать.</summary>
   public bool IsActive => CurrentSource() != null && SelectedComponent != null;

   /// <summary>Прочитанные файлы армирования схемы (можно готовить в фоновом потоке).</summary>
   public sealed record Data(
      IPlateRebarFieldSource? Selected, string? SelectedFile,
      IPlateRebarFieldSource? Assigned, string? AssignedFile,
      IReadOnlyList<(string File, string Error)> Errors,
      string Key = "");

   /// <summary>Прочитать файлы армирования схемы (ASP/RBT ЛИРЫ) и ТЗА КЭ. Без обращения к UI.</summary>
   public static Data Read(DatabaseService db, int schemaId)
   {
      IPlateRebarFieldSource? selected = null, assigned = null;
      string? selectedFile = null, assignedFile = null;
      var errors = new List<(string, string)>();
      var key = new System.Text.StringBuilder();

      if (db.GetFemSchemaSelectedReinforcementFile(schemaId) is { } asp)
      {
         key.Append(asp.FileName).Append(':').Append(Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(asp.Data))).Append('|');
         try
         {
            selected = new LiraAspPlateRebarSource(LiraAspReader.Read(asp.Data));
            selectedFile = asp.FileName;
         }
         catch (Exception ex) when (ex is InvalidDataException or IOException or ArgumentException)
         {
            errors.Add((asp.FileName, ex.Message));
         }
      }

      if (db.GetFemSchemaReinforcementFile(schemaId) is { } rbt)
      {
         key.Append(rbt.FileName).Append(':').Append(Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(rbt.Data)));
         try
         {
            var typeIds = db.GetFemMeshElements(schemaId)
               .Where(e => e.ElemType == "shell")
               .Select(e => new KeyValuePair<string, string?>(e.ElemTag, e.ReinforcementTypeIds));
            assigned = new LiraRbtPlateRebarSource(LiraRbtReader.Read(rbt.Data), typeIds);
            assignedFile = rbt.FileName;
         }
         catch (Exception ex) when (ex is InvalidDataException or IOException or ArgumentException)
         {
            errors.Add((rbt.FileName, ex.Message));
         }
      }
      return new Data(selected, selectedFile, assigned, assignedFile, errors, key.ToString());
   }

   /// <summary>Прочитать и сразу применить (в UI-потоке).</summary>
   public void Load(DatabaseService db, int schemaId) => Apply(Read(db, schemaId));

   string? _dataKey;

   /// <summary>Откуда перечитывать файлы (задаёт владелец); null — перечитывание не поддерживается.</summary>
   public Func<Data>? Reader { get; set; }

   /// <summary>Перечитать файлы, если они изменились с прошлого чтения (например, дозагружены из меню схемы).</summary>
   public void Reload()
   {
      if (Reader == null) return;
      var data = Reader();
      if (data.Key == _dataKey) return;
      Apply(data);
   }

   /// <summary>
   /// Применить прочитанные данные. Выбор источника и компоненты сохраняется, если он
   /// по-прежнему доступен.
   /// </summary>
   public void Apply(Data data)
   {
      _dataKey = data.Key;
      _selected = data.Selected; _selectedFile = data.SelectedFile;
      _assigned = data.Assigned; _assignedFile = data.AssignedFile;
      foreach (var (file, error) in data.Errors)
         _warn?.Invoke(string.Format(Loc.S("PlateRebarMosaicReadError"), file, error));

      var kind = SelectedSource?.Kind ?? PlateRebarMosaicSourceKind.None;
      var options = new List<PlateRebarMosaicSourceOption>
      {
         new(PlateRebarMosaicSourceKind.None, Loc.S("PlateRebarMosaicSourceNone")),
      };
      if (_selected != null) options.Add(new(PlateRebarMosaicSourceKind.Selected, Loc.S("PlateRebarMosaicSourceSelected")));
      if (_assigned != null) options.Add(new(PlateRebarMosaicSourceKind.Assigned, Loc.S("PlateRebarMosaicSourceAssigned")));
      if (_selected != null && _assigned != null)
         options.Add(new(PlateRebarMosaicSourceKind.Difference, Loc.S("PlateRebarMosaicSourceDifference")));
      SourceOptions = options;
      OnPropertyChanged(nameof(SourceOptions));
      OnPropertyChanged(nameof(HasData));

      _selectedSource = options.FirstOrDefault(o => o.Kind == kind) ?? options[0];
      OnPropertyChanged(nameof(SelectedSource));
      OnPropertyChanged(nameof(IsActive));
      RefreshComponents();
      RaiseChanged();
   }

   IPlateRebarFieldSource? CurrentSource() => SelectedSource?.Kind switch
   {
      PlateRebarMosaicSourceKind.Selected => _selected,
      PlateRebarMosaicSourceKind.Assigned => _assigned,
      PlateRebarMosaicSourceKind.Difference when _selected != null && _assigned != null =>
         new PlateRebarDifferenceSource(_assigned, _selected),
      _ => null,
   };

   void RefreshComponents()
   {
      var source = CurrentSource();
      var previous = SelectedComponent?.Component ?? PlateRebarMosaicComponent.BottomX;
      ComponentOptions = source == null
         ? []
         : Enum.GetValues<PlateRebarMosaicComponent>()
            .Where(source.Supports)
            .Select(c => new PlateRebarMosaicComponentOption(c, ComponentLabel(c)))
            .ToList();
      OnPropertyChanged(nameof(ComponentOptions));
      _selectedComponent = ComponentOptions.FirstOrDefault(o => o.Component == previous) ?? ComponentOptions.FirstOrDefault();
      OnPropertyChanged(nameof(SelectedComponent));
      OnPropertyChanged(nameof(IsActive));
   }

   static string ComponentLabel(PlateRebarMosaicComponent c) => Loc.S(c switch
   {
      PlateRebarMosaicComponent.BottomX => "PlateRebarMosaicCompAs1",
      PlateRebarMosaicComponent.TopX => "PlateRebarMosaicCompAs2",
      PlateRebarMosaicComponent.BottomY => "PlateRebarMosaicCompAs3",
      PlateRebarMosaicComponent.TopY => "PlateRebarMosaicCompAs4",
      _ => "PlateRebarMosaicCompAsw",
   });

   void RaiseChanged() => Changed?.Invoke(this, EventArgs.Empty);

   /// <summary>
   /// Раскрасить КЭ по текущим настройкам и обновить легенду (шкала — по этим КЭ).
   /// Null — мозаика выключена.
   /// </summary>
   public PlateRebarMosaicColoring? Compute(IEnumerable<string> elemTags)
   {
      var source = CurrentSource();
      if (source == null || SelectedComponent is not { } comp)
      {
         Legend = [];
         LegendTitle = "";
         OnPropertyChanged(nameof(Legend));
         return null;
      }

      bool diverging = SelectedSource!.Kind == PlateRebarMosaicSourceKind.Difference;
      var values = PlateRebarMosaic.Evaluate(source, comp.Component, elemTags);
      var numbers = values.Values.Where(v => v.Value != null).Select(v => v.Value!.Value).ToList();
      var manual = ParseThresholds(ThresholdsText);
      var scale = manual.Count > 0
         ? PlateRebarMosaicScale.Manual(manual, diverging)
         : PlateRebarMosaicScale.Auto(numbers, diverging);
      var colors = BandColors(scale);

      var counts = new int[scale.Bands.Count];
      int failures = 0, missing = 0;
      var byTag = new Dictionary<string, Color>(StringComparer.Ordinal);
      foreach (var (tag, v) in values)
      {
         if (v.FailureCode != null) { byTag[tag] = FailureColor; failures++; }
         else if (v.Value is { } x) { int b = scale.BandOf(x); counts[b]++; byTag[tag] = colors[b]; }
         else missing++;
      }

      double min = numbers.Count > 0 ? numbers.Min() : 0, max = numbers.Count > 0 ? numbers.Max() : 0;
      var legend = new List<PlateRebarMosaicLegendItem>();
      for (int i = scale.Bands.Count - 1; i >= 0; i--)
         legend.Add(new(Freeze(colors[i]), BandLabel(scale.Bands[i], min, max), counts[i]));
      if (failures > 0) legend.Add(new(Freeze(FailureColor), Loc.S("PlateRebarMosaicFailure"), failures));
      if (missing > 0) legend.Add(new(Freeze(Fem3DVM.ShellBgColor), Loc.S("PlateRebarMosaicNoData"), missing));
      Legend = legend;
      OnPropertyChanged(nameof(Legend));

      string unit = comp.Component == PlateRebarMosaicComponent.Transverse ? "" : Loc.S("PlateRebarMosaicUnit");
      string file = SelectedSource.Kind switch
      {
         PlateRebarMosaicSourceKind.Selected => _selectedFile ?? "",
         PlateRebarMosaicSourceKind.Assigned => _assignedFile ?? "",
         _ => $"{_assignedFile} − {_selectedFile}",
      };
      LegendTitle = $"{comp.Label}{unit}\n{SelectedSource.Label}\n{file}";
      return new PlateRebarMosaicColoring(byTag);
   }

   static SolidColorBrush Freeze(Color c)
   {
      var b = new SolidColorBrush(c);
      b.Freeze();
      return b;
   }

   /// <summary>Пороги из строки «3,93; 5,65 7,85»: разделители «;» и пробел, запятая — десятичная.</summary>
   public static List<double> ParseThresholds(string? text)
   {
      var result = new List<double>();
      if (string.IsNullOrWhiteSpace(text)) return result;
      foreach (string token in text.Split([';', ' ', '\t'], StringSplitOptions.RemoveEmptyEntries))
         if (double.TryParse(token.Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out double v))
            result.Add(v);
      return result;
   }

   static Color[] BandColors(PlateRebarMosaicScale scale)
   {
      var bands = scale.Bands;
      var colors = new Color[bands.Count];
      if (!scale.IsDiverging)
      {
         colors[0] = ZeroColor;
         for (int i = 1; i < bands.Count; i++)
            colors[i] = Interpolate(SequentialStops, bands.Count <= 2 ? 1 : (i - 1) / (double)(bands.Count - 2));
         return colors;
      }
      int neg = bands.Count(b => b.IsNegative), pos = bands.Count(b => b.IsPositive);
      int ni = 0, pi = 0;
      for (int i = 0; i < bands.Count; i++)
      {
         if (bands[i].IsNegative) colors[i] = Interpolate(DeficitStops, neg <= 1 ? 0 : ni++ / (double)(neg - 1));
         else if (bands[i].IsPositive) colors[i] = Interpolate(ReserveStops, pos <= 1 ? 1 : pi++ / (double)(pos - 1));
         else colors[i] = ZeroColor;
      }
      return colors;
   }

   static Color Interpolate(Color[] stops, double t)
   {
      t = Math.Clamp(t, 0, 1) * (stops.Length - 1);
      int i = Math.Min((int)t, stops.Length - 2);
      double f = t - i;
      byte L(byte a, byte b) => (byte)Math.Round(a + (b - a) * f);
      return Color.FromRgb(L(stops[i].R, stops[i + 1].R), L(stops[i].G, stops[i + 1].G), L(stops[i].B, stops[i + 1].B));
   }

   static string BandLabel(PlateRebarMosaicBand band, double min, double max)
   {
      static string F(double v) => v.ToString("0.##", CultureInfo.CurrentCulture);
      if (band.IsZero) return "0";
      if (double.IsNegativeInfinity(band.Lower)) return $"{F(Math.Min(min, band.Upper))} … {F(band.Upper)}";
      if (double.IsPositiveInfinity(band.Upper)) return $"{F(band.Lower)} … {F(Math.Max(max, band.Lower))}";
      return $"{F(band.Lower)} … {F(band.Upper)}";
   }
}
